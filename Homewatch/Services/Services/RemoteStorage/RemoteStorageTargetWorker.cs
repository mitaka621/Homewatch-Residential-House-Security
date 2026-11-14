using Homewatch.Contracts.Abstractions.Repositories;
using Homewatch.Contracts.Abstractions.Services;
using Homewatch.Contracts.Dtos.RemoteStorageDtos;
using Homewatch.Core.Utilities;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading.Channels;

namespace Homewatch.Core.Services.RemoteStorage
{
    public class RemoteStorageTargetWorker
    {
        private const string _partialFileExtension = ".partial";
        private const double _bytesPerMegabyte = 1024 * 1024;

        private static readonly TimeSpan _copyStatisticsInterval = TimeSpan.FromMinutes(1);

        private readonly Channel<VideoChunkReplicationItemDto> _channel = Channel.CreateUnbounded<VideoChunkReplicationItemDto>();
        private readonly HashSet<string> _createdDirectories = new(StringComparer.OrdinalIgnoreCase);

        private readonly IRemoteStorageTargetRepository _remoteStorageTargetRepository;
        private readonly IVideoChunkReplicationRepository _videoChunkReplicationRepository;
        private readonly ISmbConnectionService _smbConnectionService;
        private readonly IStorageLocationService _storageLocationService;
        private readonly ILogger<RemoteStorageTargetWorker> _logger;
        private readonly TimeSpan _retryDelay;
        private readonly TimeSpan? _retention;
        private readonly CancellationTokenSource _cancellationTokenSource;

        private Task _processingTask = Task.CompletedTask;
        private ISmbShareSession? _session;
        private bool _isFailing;

        private int _copiedChunkCount;
        private long _copiedBytes;
        private TimeSpan _totalCopyDuration;
        private TimeSpan _totalUploadDuration;
        private TimeSpan _totalRenameDuration;
        private TimeSpan _slowestCopyDuration;
        private DateTime _copyStatisticsPeriodStartUtc = DateTime.UtcNow;

        public RemoteStorageTargetWorker(
            RemoteStorageTargetDto target,
            IRemoteStorageTargetRepository remoteStorageTargetRepository,
            ISmbConnectionService smbConnectionService,
            IStorageLocationService storageLocationService,
            ILogger<RemoteStorageTargetWorker> logger,
            TimeSpan retryDelay,
            CancellationToken stoppingToken,
            IVideoChunkReplicationRepository videoChunkReplicationRepository)
        {
            Target = target;
            _remoteStorageTargetRepository = remoteStorageTargetRepository;
            _smbConnectionService = smbConnectionService;
            _storageLocationService = storageLocationService;
            _logger = logger;
            _retryDelay = retryDelay;
            _retention = target.Retention.ToTimeSpan();
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _videoChunkReplicationRepository = videoChunkReplicationRepository;
        }

        public RemoteStorageTargetDto Target { get; }

        public void Start()
        {
            _processingTask = Task.Run(() => ProcessQueueAsync(_cancellationTokenSource.Token));
        }

        public void Enqueue(VideoChunkReplicationItemDto item)
        {
            _channel.Writer.TryWrite(item);
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
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await foreach (var replicationItem in _channel.Reader.ReadAllAsync(cancellationToken))
                    {
                        await ReplicateVideoChunkAsync(replicationItem, cancellationToken);
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
                await SmbOperationRunner.RunAsync(CloseSession);
            }
        }

        private async Task ReplicateVideoChunkAsync(VideoChunkReplicationItemDto item, CancellationToken cancellationToken)
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
                        await SmbOperationRunner.RunAsync(() => CopyVideoChunk(localChunkPath, item), cancellationToken);
                    }

                    await _videoChunkReplicationRepository.MarkVideoChunkReplicatedAsync(Target.Id, item.VideoChunkId);

                    if (_isFailing)
                    {
                        _isFailing = false;
                        _logger.LogInformation("Video sync to {SharePath} recovered.", Target.SharePath);
                    }

                    LogCopyStatisticsIfDue();

                    return;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    await SmbOperationRunner.RunAsync(CloseSession);

                    if (!_isFailing)
                    {
                        _isFailing = true;
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

        private void CopyVideoChunk(string localChunkPath, VideoChunkReplicationItemDto item)
        {
            var copyStartTimestamp = Stopwatch.GetTimestamp();
            var session = GetSession();
            var connectDuration = Stopwatch.GetElapsedTime(copyStartTimestamp);

            var dayFolderName = Path.GetFileName(Path.GetDirectoryName(localChunkPath)) ?? string.Empty;
            var remoteDirectory = $@"{item.DeviceId}\{dayFolderName}";
            var remoteChunkPath = $@"{remoteDirectory}\{item.FileName}";
            var partialRemoteChunkPath = remoteChunkPath + _partialFileExtension;

            if (_createdDirectories.Add(remoteDirectory))
            {
                session.CreateDirectory(remoteDirectory);
            }

            long chunkSizeInBytes;
            var uploadStartTimestamp = Stopwatch.GetTimestamp();

            using (var source = new FileStream(localChunkPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                chunkSizeInBytes = source.Length;
                session.UploadFile(source, partialRemoteChunkPath);
            }

            var uploadDuration = Stopwatch.GetElapsedTime(uploadStartTimestamp);
            var renameStartTimestamp = Stopwatch.GetTimestamp();

            session.RenameFile(partialRemoteChunkPath, remoteChunkPath);

            var renameDuration = Stopwatch.GetElapsedTime(renameStartTimestamp);
            var copyDuration = Stopwatch.GetElapsedTime(copyStartTimestamp);

            RecordCopy(copyDuration, uploadDuration, renameDuration, chunkSizeInBytes);

            _logger.LogInformation(
                "Video sync to {SharePath}: copied {FileName} ({SizeInMegabytes:0.00} megabytes) in {CopyMilliseconds:0} milliseconds (connect {ConnectMilliseconds:0}, upload {UploadMilliseconds:0}, rename {RenameMilliseconds:0}), {DelayAfterRecordingSeconds:0.0} seconds after the chunk ended, {PendingChunkCount} chunks waiting.",
                Target.SharePath,
                item.FileName,
                chunkSizeInBytes / _bytesPerMegabyte,
                copyDuration.TotalMilliseconds,
                connectDuration.TotalMilliseconds,
                uploadDuration.TotalMilliseconds,
                renameDuration.TotalMilliseconds,
                (DateTime.UtcNow - item.ChunkEndTime).TotalSeconds,
                _channel.Reader.Count);
        }

        private void RecordCopy(TimeSpan copyDuration, TimeSpan uploadDuration, TimeSpan renameDuration, long chunkSizeInBytes)
        {
            _copiedChunkCount++;
            _copiedBytes += chunkSizeInBytes;
            _totalCopyDuration += copyDuration;
            _totalUploadDuration += uploadDuration;
            _totalRenameDuration += renameDuration;

            if (copyDuration > _slowestCopyDuration)
            {
                _slowestCopyDuration = copyDuration;
            }
        }

        private void LogCopyStatisticsIfDue()
        {
            var elapsed = DateTime.UtcNow - _copyStatisticsPeriodStartUtc;

            if (elapsed < _copyStatisticsInterval || _copiedChunkCount == 0)
            {
                return;
            }

            _logger.LogInformation(
                "Video sync to {SharePath}: {ChunkCount} chunks copied in the last {Minutes:0.#} minutes. Average copy {AverageCopyMilliseconds:0} milliseconds (upload {AverageUploadMilliseconds:0}, rename {AverageRenameMilliseconds:0}), slowest {SlowestCopyMilliseconds:0} milliseconds, average size {AverageSizeInMegabytes:0.00} megabytes, upload speed {UploadMegabytesPerSecond:0.00} megabytes per second, {PendingChunkCount} chunks waiting.",
                Target.SharePath,
                _copiedChunkCount,
                elapsed.TotalMinutes,
                _totalCopyDuration.TotalMilliseconds / _copiedChunkCount,
                _totalUploadDuration.TotalMilliseconds / _copiedChunkCount,
                _totalRenameDuration.TotalMilliseconds / _copiedChunkCount,
                _slowestCopyDuration.TotalMilliseconds,
                _copiedBytes / _bytesPerMegabyte / _copiedChunkCount,
                _copiedBytes / _bytesPerMegabyte / Math.Max(_totalUploadDuration.TotalSeconds, 0.001),
                _channel.Reader.Count);

            _copiedChunkCount = 0;
            _copiedBytes = 0;
            _totalCopyDuration = TimeSpan.Zero;
            _totalUploadDuration = TimeSpan.Zero;
            _totalRenameDuration = TimeSpan.Zero;
            _slowestCopyDuration = TimeSpan.Zero;
            _copyStatisticsPeriodStartUtc = DateTime.UtcNow;
        }

        private bool IsExpiredOnTarget(DateTime chunkEndTimeUtc)
        {
            return _retention != null && chunkEndTimeUtc < DateTime.UtcNow - _retention.Value;
        }

        private ISmbShareSession GetSession()
        {
            if (_session is { IsConnected: true })
            {
                return _session;
            }

            CloseSession();

            _session = _smbConnectionService.OpenSession(Target);

            return _session;
        }

        private void CloseSession()
        {
            _session?.Dispose();
            _session = null;
            _createdDirectories.Clear();
        }
    }
}
