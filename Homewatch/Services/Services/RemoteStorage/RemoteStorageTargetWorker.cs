using Homewatch.Contracts.Abstractions.Repositories;
using Homewatch.Contracts.Abstractions.Services;
using Homewatch.Contracts.Constants;
using Homewatch.Contracts.Dtos.NotificationDtos;
using Homewatch.Contracts.Dtos.RemoteStorageDtos;
using Homewatch.Contracts.Enums;
using Homewatch.Core.Utilities;
using Microsoft.Extensions.Logging;
using MudBlazor;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

namespace Homewatch.Core.Services.RemoteStorage
{
    public class RemoteStorageTargetWorker
    {
        private const int _maximumNotificationDescriptionLength = 500;

        private readonly TimeSpan _syncLoadEvaluationInterval = TimeSpan.FromMinutes(1);
        private readonly TimeSpan _overloadNotificationDelay = TimeSpan.FromMinutes(10);
        private readonly TimeSpan _repeatedFailureLogInterval = TimeSpan.FromMinutes(10);

        private readonly Channel<VideoChunkReplicationItemDto> _channel = Channel.CreateUnbounded<VideoChunkReplicationItemDto>();
        private readonly ConcurrentDictionary<Guid, byte> _queuedVideoChunkIds = new();
        private readonly RemoteStorageSyncLoadTracker _syncLoadTracker = new();
        private readonly object _syncLoadEvaluationLock = new();
        private readonly object _failureLogLock = new();

        private readonly IRemoteStorageTargetRepository _remoteStorageTargetRepository;
        private readonly IVideoChunkReplicationRepository _videoChunkReplicationRepository;
        private readonly ISmbConnectionService _smbConnectionService;
        private readonly IStorageLocationService _storageLocationService;
        private readonly INotificationsService _notificationsService;
        private readonly VideoRecordingRateTracker _recordingRateTracker;
        private readonly ILogger<RemoteStorageTargetWorker> _logger;
        private readonly TimeSpan _retryDelay;
        private readonly TimeSpan? _retention;
        private readonly int _parallelCopies;
        private readonly CancellationTokenSource _cancellationTokenSource;

        private Task _processingTask = Task.CompletedTask;
        private int _activeChunkCount;
        private int _isFailing;
        private DateTime _lastSyncLoadEvaluationUtc = DateTime.UtcNow;
        private DateTime _lastFailureLogUtc = DateTime.MinValue;
        private DateTime? _overloadedSinceUtc;
        private bool _isOverloadNotified;

        public RemoteStorageTargetWorker(
            RemoteStorageTargetDto target,
            IRemoteStorageTargetRepository remoteStorageTargetRepository,
            ISmbConnectionService smbConnectionService,
            IStorageLocationService storageLocationService,
            INotificationsService notificationsService,
            VideoRecordingRateTracker recordingRateTracker,
            ILogger<RemoteStorageTargetWorker> logger,
            TimeSpan retryDelay,
            int parallelCopies,
            CancellationToken stoppingToken,
            IVideoChunkReplicationRepository videoChunkReplicationRepository)
        {
            Target = target;
            _remoteStorageTargetRepository = remoteStorageTargetRepository;
            _smbConnectionService = smbConnectionService;
            _storageLocationService = storageLocationService;
            _notificationsService = notificationsService;
            _recordingRateTracker = recordingRateTracker;
            _logger = logger;
            _retryDelay = retryDelay;
            _parallelCopies = parallelCopies;
            _retention = target.Retention.ToTimeSpan();
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _videoChunkReplicationRepository = videoChunkReplicationRepository;
        }

        public RemoteStorageTargetDto Target { get; }

        public void Start()
        {
            _processingTask = Task.WhenAll(Enumerable.Range(0, _parallelCopies)
                .Select(_ => Task.Run(() => ProcessQueueAsync(_cancellationTokenSource.Token))));
        }

        public void Enqueue(VideoChunkReplicationItemDto item)
        {
            // A chunk saved while the worker starts arrives both from the pending chunks and from recording, and two parallel copies of one file collide on the share.
            if (_queuedVideoChunkIds.TryAdd(item.VideoChunkId, 0) && !_channel.Writer.TryWrite(item))
            {
                _queuedVideoChunkIds.TryRemove(item.VideoChunkId, out _);
            }
        }

        public RemoteStorageSyncStatusDto GetSyncStatus()
        {
            return _syncLoadTracker.GetStatus(
                Target.Id,
                _parallelCopies,
                _channel.Reader.Count + Volatile.Read(ref _activeChunkCount),
                Volatile.Read(ref _isFailing) == 1,
                _recordingRateTracker.GetBytesPerSecond());
        }

        public async Task StopAsync(TimeSpan timeout)
        {
            _channel.Writer.TryComplete();
            await _cancellationTokenSource.CancelAsync();

            try
            {
                await _processingTask.WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning(
                    "Video sync to {SharePath} did not stop within {Timeout} seconds and is left to finish in the background.",
                    Target.SharePath,
                    timeout.TotalSeconds);

                return;
            }
            catch (OperationCanceledException)
            {
            }

            _cancellationTokenSource.Dispose();
        }

        private async Task ProcessQueueAsync(CancellationToken cancellationToken)
        {
            var copySession = new RemoteStorageCopySession(_smbConnectionService, Target);

            try
            {
                await foreach (var replicationItem in _channel.Reader.ReadAllAsync(cancellationToken))
                {
                    Interlocked.Increment(ref _activeChunkCount);

                    try
                    {
                        await ReplicateVideoChunkAsync(copySession, replicationItem, cancellationToken);
                    }
                    finally
                    {
                        _queuedVideoChunkIds.TryRemove(replicationItem.VideoChunkId, out _);
                        Interlocked.Decrement(ref _activeChunkCount);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Video sync to {SharePath} stopped unexpectedly.", Target.SharePath);
            }
            finally
            {
                await SmbOperationRunner.RunAsync(copySession.Close);
            }
        }

        private async Task ReplicateVideoChunkAsync(RemoteStorageCopySession copySession, VideoChunkReplicationItemDto item, CancellationToken cancellationToken)
        {
            if (!_storageLocationService.TryGetCameraChunkFullPath(item.DeviceId.ToString(), item.FileName, out var localChunkPath))
            {
                return;
            }

            while (File.Exists(localChunkPath))
            {
                try
                {
                    if (await _videoChunkReplicationRepository.IsVideoChunkReplicatedAsync(Target.Id, item.VideoChunkId))
                    {
                        return;
                    }

                    if (!IsExpiredOnTarget(item.ChunkEndTime))
                    {
                        var copyStartTimestamp = Stopwatch.GetTimestamp();

                        var chunkSizeInBytes = await SmbOperationRunner.RunAsync(() => copySession.CopyVideoChunk(localChunkPath, item), cancellationToken);

                        _syncLoadTracker.RecordCopy(Stopwatch.GetElapsedTime(copyStartTimestamp), chunkSizeInBytes, item.ChunkEndTime);
                    }

                    await _videoChunkReplicationRepository.MarkVideoChunkReplicatedAsync(Target.Id, item.VideoChunkId);

                    if (Interlocked.Exchange(ref _isFailing, 0) == 1)
                    {
                        _logger.LogInformation("Video sync to {SharePath} recovered.", Target.SharePath);
                    }

                    await EvaluateSyncLoadIfDueAsync();

                    return;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    await SmbOperationRunner.RunAsync(copySession.Close);

                    if (IsFailureLogDue(Interlocked.Exchange(ref _isFailing, 1) == 1))
                    {
                        _logger.LogWarning(
                            exception,
                            "Copying video chunk {FileName} to {SharePath} failed. Retrying every {RetryDelay} seconds.",
                            item.FileName,
                            Target.SharePath,
                            _retryDelay.TotalSeconds);
                    }

                    await Task.Delay(_retryDelay, cancellationToken);
                }
            }
        }

        private bool IsFailureLogDue(bool wasAlreadyFailing)
        {
            lock (_failureLogLock)
            {
                var now = DateTime.UtcNow;

                if (wasAlreadyFailing && now - _lastFailureLogUtc < _repeatedFailureLogInterval)
                {
                    return false;
                }

                _lastFailureLogUtc = now;

                return true;
            }
        }

        private async Task EvaluateSyncLoadIfDueAsync()
        {
            RemoteStorageSyncStatusDto syncStatus;

            lock (_syncLoadEvaluationLock)
            {
                var now = DateTime.UtcNow;

                if (now - _lastSyncLoadEvaluationUtc < _syncLoadEvaluationInterval)
                {
                    return;
                }

                _lastSyncLoadEvaluationUtc = now;
                syncStatus = GetSyncStatus();

                if (syncStatus.State != RemoteStorageSyncState.Overloaded)
                {
                    _overloadedSinceUtc = null;
                    _isOverloadNotified = false;

                    return;
                }

                _overloadedSinceUtc ??= now;

                if (_isOverloadNotified || now - _overloadedSinceUtc < _overloadNotificationDelay)
                {
                    return;
                }

                _isOverloadNotified = true;
            }

            _logger.LogWarning(
                "Video sync to {SharePath} cannot keep up: cameras record {RecordingMegabytesPerSecond:0.00} megabytes per second, {UploadMegabytesPerSecond:0.00} reach the share, {PendingChunkCount} chunks waiting.",
                Target.SharePath,
                syncStatus.RecordingMegabytesPerSecond,
                syncStatus.UploadMegabytesPerSecond,
                syncStatus.PendingChunkCount);

            try
            {
                await _notificationsService.PushNotificationToAllUsersAsync(new CreateNotificationDto
                {
                    Name = "Video sync cannot keep up",
                    Description = BuildOverloadNotificationDescription(),
                    Icon = Icons.Material.Filled.SyncProblem,
                    IconColor = Color.Warning,
                    IsUnique = true,
                    NotificationType = NotificationType.System,
                    RelativeRedirectUrl = AppRoutes.RetentionSettings
                });
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to send the video sync overload notification for {SharePath}.", Target.SharePath);
            }
        }

        private string BuildOverloadNotificationDescription()
        {
            var description = DescribeOverload(Target.SharePath);

            return description.Length <= _maximumNotificationDescriptionLength
                ? description
                : DescribeOverload("the share");
        }

        private string DescribeOverload(string shareName)
        {
            return $"The cameras record more video than {shareName} receives, so footage reaches it later and later. "
                + "First check the upload speed of the server's network. If it has room to spare, raise Parallel copies per share in the Video Retention settings without using up the whole upload. "
                + "If the network is already full, lower the resolution or quality of the cameras that record the most.";
        }

        private bool IsExpiredOnTarget(DateTime chunkEndTimeUtc)
        {
            return _retention != null && chunkEndTimeUtc < DateTime.UtcNow - _retention.Value;
        }
    }
}
