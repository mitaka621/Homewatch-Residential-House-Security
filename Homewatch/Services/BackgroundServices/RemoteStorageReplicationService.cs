using Homewatch.Contracts.Abstractions.Repositories;
using Homewatch.Contracts.Abstractions.Services;
using Homewatch.Contracts.Dtos.RemoteStorageDtos;
using Homewatch.Core.Services.RemoteStorage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Homewatch.Core.BackgroundServices
{
    public class RemoteStorageReplicationService : BackgroundService, IChunkReplicationQueue, IRemoteStorageSyncMonitor, IRemoteStorageSyncController
    {
        private readonly IRemoteStorageTargetRepository _remoteStorageTargetRepository;
        private readonly IVideoChunkRepository _videoChunkRepository;
        private readonly ISmbConnectionService _smbConnectionService;
        private readonly IStorageLocationService _storageLocationService;
        private readonly INotificationsService _notificationsService;
        private readonly ISystemSettingsService _systemSettingsService;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<RemoteStorageReplicationService> _logger;
        private readonly IVideoChunkReplicationRepository _videoChunkReplicationRepository;
        private readonly int _settingsCheckIntervalInChunks;
        private readonly TimeSpan _retryDelay;
        private readonly TimeSpan _workerStopTimeout = TimeSpan.FromSeconds(5);
        private readonly ConcurrentDictionary<Guid, RemoteStorageTargetWorker> _workers = new();
        private readonly VideoRecordingRateTracker _recordingRateTracker = new();
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private readonly Channel<bool> _refreshRequests = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite
        });

        private int _parallelCopiesPerShare;

        public RemoteStorageReplicationService(
            IRemoteStorageTargetRepository remoteStorageTargetRepository,
            ISmbConnectionService smbConnectionService,
            IStorageLocationService storageLocationService,
            INotificationsService notificationsService,
            ISystemSettingsService systemSettingsService,
            IConfiguration configuration,
            ILoggerFactory loggerFactory,
            ILogger<RemoteStorageReplicationService> logger,
            IVideoChunkRepository videoChunkRepository,
            IVideoChunkReplicationRepository videoChunkReplicationRepository)
        {
            _remoteStorageTargetRepository = remoteStorageTargetRepository;
            _smbConnectionService = smbConnectionService;
            _storageLocationService = storageLocationService;
            _notificationsService = notificationsService;
            _systemSettingsService = systemSettingsService;
            _loggerFactory = loggerFactory;
            _logger = logger;
            _videoChunkRepository = videoChunkRepository;
            _videoChunkReplicationRepository = videoChunkReplicationRepository;

            _settingsCheckIntervalInChunks = int.Parse(
                configuration.GetSection("RemoteStorageConfig")["SettingsCheckIntervalInChunks"]
                ?? throw new ArgumentNullException("SettingsCheckIntervalInChunks not configured"));

            _retryDelay = TimeSpan.FromSeconds(int.Parse(
                configuration.GetSection("RemoteStorageConfig")["RetryDelayInSeconds"]
                ?? throw new ArgumentNullException("RetryDelayInSeconds not configured")));
        }

        private readonly Channel<VideoChunkReplicationItemDto> _channel = Channel.CreateUnbounded<VideoChunkReplicationItemDto>();

        public void Enqueue(VideoChunkReplicationItemDto item)
        {
            _channel.Writer.TryWrite(item);
        }

        public RemoteStorageSyncStatusDto? GetSyncStatus(Guid targetId)
        {
            return _workers.TryGetValue(targetId, out var worker)
                ? worker.GetSyncStatus()
                : null;
        }

        public List<CameraRecordingRateDto> GetCameraRecordingRates()
        {
            return _recordingRateTracker.GetCameraRecordingRates();
        }

        public void RequestRefresh()
        {
            _refreshRequests.Writer.TryWrite(true);
        }

        public async Task StopSyncAsync(Guid targetId)
        {
            await _refreshLock.WaitAsync();

            try
            {
                if (_workers.TryGetValue(targetId, out var worker))
                {
                    await StopWorkerAsync(worker);
                }
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await RefreshTargetsAsync(stoppingToken);

            var refreshRequestsTask = ProcessRefreshRequestsAsync(stoppingToken);
            var savedChunksSinceLastCheck = 0;

            try
            {
                await foreach (var item in _channel.Reader.ReadAllAsync(stoppingToken))
                {
                    RecordRecordedChunk(item);

                    savedChunksSinceLastCheck++;

                    if (savedChunksSinceLastCheck >= _settingsCheckIntervalInChunks)
                    {
                        savedChunksSinceLastCheck = 0;
                        await RefreshTargetsAsync(stoppingToken);
                    }

                    foreach (var worker in _workers.Values)
                    {
                        worker.Enqueue(item);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            finally
            {
                await refreshRequestsTask;

                foreach (var worker in _workers.Values)
                {
                    await worker.StopAsync(_workerStopTimeout);
                }

                _workers.Clear();
            }
        }

        private async Task ProcessRefreshRequestsAsync(CancellationToken stoppingToken)
        {
            try
            {
                await foreach (var _ in _refreshRequests.Reader.ReadAllAsync(stoppingToken))
                {
                    await RefreshTargetsAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }

        private async Task RefreshTargetsAsync(CancellationToken stoppingToken)
        {
            await _refreshLock.WaitAsync(stoppingToken);

            try
            {
                Dictionary<Guid, RemoteStorageTargetDto> enabledTargets;
                int parallelCopiesPerShare;

                try
                {
                    enabledTargets = await _remoteStorageTargetRepository.GetEnabledAsync();
                    parallelCopiesPerShare = Math.Max(1, (await _systemSettingsService.GetSystemSettingsAsync()).ParallelCopiesPerShare);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to load remote storage settings. Keeping the current video sync targets.");
                    return;
                }

                var hasParallelCopiesChanged = parallelCopiesPerShare != _parallelCopiesPerShare;

                _parallelCopiesPerShare = parallelCopiesPerShare;

                foreach (var worker in _workers.Values.ToList())
                {
                    if (!hasParallelCopiesChanged
                        && enabledTargets.TryGetValue(worker.Target.Id, out var storedTarget)
                        && !HasTargetChanged(worker.Target, storedTarget))
                    {
                        continue;
                    }

                    await StopWorkerAsync(worker);
                }

                foreach (var target in enabledTargets.Values.Where(x => !_workers.ContainsKey(x.Id)))
                {
                    await StartWorkerAsync(target, stoppingToken);
                }
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        private async Task StartWorkerAsync(RemoteStorageTargetDto target, CancellationToken stoppingToken)
        {
            var worker = new RemoteStorageTargetWorker(
                target,
                _remoteStorageTargetRepository,
                _smbConnectionService,
                _storageLocationService,
                _notificationsService,
                _recordingRateTracker,
                _loggerFactory.CreateLogger<RemoteStorageTargetWorker>(),
                _retryDelay,
                _parallelCopiesPerShare,
                stoppingToken,
                _videoChunkReplicationRepository);

            _workers[target.Id] = worker;
            worker.Start();

            try
            {
                var unreplicatedVideoChunks = await _videoChunkRepository.GetUnreplicatedVideoChunksAsync(target.Id);

                foreach (var unreplicatedVideoChunk in unreplicatedVideoChunks)
                {
                    worker.Enqueue(unreplicatedVideoChunk);
                }

                _logger.LogInformation(
                    "Started video sync to {SharePath} with {Count} pending chunks and {ParallelCopies} parallel copies.",
                    target.SharePath,
                    unreplicatedVideoChunks.Count,
                    _parallelCopiesPerShare);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to load pending video chunks for {SharePath}. Retrying on the next settings check.", target.SharePath);

                await StopWorkerAsync(worker);
            }
        }

        private async Task StopWorkerAsync(RemoteStorageTargetWorker worker)
        {
            await worker.StopAsync(_workerStopTimeout);

            _workers.TryRemove(worker.Target.Id, out _);

            _logger.LogInformation("Stopped video sync to {SharePath}.", worker.Target.SharePath);
        }

        private void RecordRecordedChunk(VideoChunkReplicationItemDto item)
        {
            if (!_storageLocationService.TryGetCameraChunkFullPath(item.DeviceId.ToString(), item.FileName, out var chunkPath))
            {
                return;
            }

            var chunkFile = new FileInfo(chunkPath);

            if (chunkFile.Exists)
            {
                _recordingRateTracker.RecordChunk(item.DeviceId, chunkFile.Length);
            }
        }

        private static bool HasTargetChanged(RemoteStorageTargetDto runningTarget, RemoteStorageTargetDto storedTarget)
        {
            return !string.Equals(runningTarget.SharePath, storedTarget.SharePath, StringComparison.OrdinalIgnoreCase)
                || runningTarget.Username != storedTarget.Username
                || runningTarget.ProtectedPassword != storedTarget.ProtectedPassword
                || runningTarget.Retention != storedTarget.Retention;
        }
    }
}
