using Homewatch.Contracts.Dtos.RemoteStorageDtos;
using Homewatch.Contracts.Enums;

namespace Homewatch.Core.Services.RemoteStorage
{
    public class RemoteStorageSyncLoadTracker
    {
        private const double _bytesPerMegabyte = 1024 * 1024;
        private const double _nearCapacityLoadPercent = 75;
        private const double _overloadedLoadPercent = 90;
        private const int _backlogChunksPerParallelCopy = 2;
        private const double _catchUpUploadToRecordingRatio = 1.05;

        private readonly TimeSpan _window = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _minimumMeasuredTime = TimeSpan.FromMinutes(1);
        private readonly DateTime _startedAtUtc = DateTime.UtcNow;
        private readonly object _lock = new();
        private readonly Queue<VideoChunkCopyDto> _copies = new();

        private TimeSpan? _latestDelayAfterRecording;

        public void RecordCopy(TimeSpan duration, long sizeInBytes, DateTime chunkEndTimeUtc)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;

                _copies.Enqueue(new VideoChunkCopyDto
                {
                    CompletedAtUtc = now,
                    Duration = duration,
                    SizeInBytes = sizeInBytes
                });
                _latestDelayAfterRecording = now - chunkEndTimeUtc;

                RemoveExpiredCopies(now);
            }
        }

        public RemoteStorageSyncStatusDto GetStatus(Guid targetId, int parallelCopies, int pendingChunkCount, bool isFailing, double recordingBytesPerSecond)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;

                RemoveExpiredCopies(now);

                var measuredSeconds = Math.Min(_window.TotalSeconds, (now - _startedAtUtc).TotalSeconds);
                var copiedBytes = _copies.Sum(x => x.SizeInBytes);
                var uploadBytesPerSecond = copiedBytes / Math.Max(measuredSeconds, 1);
                var loadPercent = Math.Min(100, _copies.Sum(x => x.Duration.TotalSeconds) / Math.Max(measuredSeconds * parallelCopies, 1) * 100);
                var state = GetState(isFailing, measuredSeconds, loadPercent, pendingChunkCount, parallelCopies, uploadBytesPerSecond, recordingBytesPerSecond);

                return new RemoteStorageSyncStatusDto
                {
                    TargetId = targetId,
                    State = state,
                    ParallelCopies = parallelCopies,
                    LoadPercent = loadPercent,
                    RecordingMegabytesPerSecond = recordingBytesPerSecond / _bytesPerMegabyte,
                    UploadMegabytesPerSecond = uploadBytesPerSecond / _bytesPerMegabyte,
                    PendingChunkCount = pendingChunkCount,
                    DelayAfterRecording = _latestDelayAfterRecording,
                    CatchUpTimeRemaining = state == RemoteStorageSyncState.CatchingUp && _copies.Count > 0
                        ? TimeSpan.FromSeconds(pendingChunkCount * ((double)copiedBytes / _copies.Count) / (uploadBytesPerSecond - recordingBytesPerSecond))
                        : null
                };
            }
        }

        private RemoteStorageSyncState GetState(
            bool isFailing,
            double measuredSeconds,
            double loadPercent,
            int pendingChunkCount,
            int parallelCopies,
            double uploadBytesPerSecond,
            double recordingBytesPerSecond)
        {
            if (isFailing)
            {
                return RemoteStorageSyncState.Unreachable;
            }

            if (measuredSeconds < _minimumMeasuredTime.TotalSeconds)
            {
                return RemoteStorageSyncState.Measuring;
            }

            var hasBacklog = pendingChunkCount > parallelCopies * _backlogChunksPerParallelCopy;

            if (hasBacklog && uploadBytesPerSecond > recordingBytesPerSecond * _catchUpUploadToRecordingRatio)
            {
                return RemoteStorageSyncState.CatchingUp;
            }

            if (hasBacklog && loadPercent >= _overloadedLoadPercent)
            {
                return RemoteStorageSyncState.Overloaded;
            }

            return loadPercent >= _nearCapacityLoadPercent
                ? RemoteStorageSyncState.NearCapacity
                : RemoteStorageSyncState.Healthy;
        }

        private void RemoveExpiredCopies(DateTime now)
        {
            while (_copies.Count > 0 && now - _copies.Peek().CompletedAtUtc > _window)
            {
                _copies.Dequeue();
            }
        }
    }
}
