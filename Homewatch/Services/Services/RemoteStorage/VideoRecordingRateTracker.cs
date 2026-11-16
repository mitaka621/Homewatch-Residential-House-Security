using Homewatch.Contracts.Dtos.RemoteStorageDtos;

namespace Homewatch.Core.Services.RemoteStorage
{
    public class VideoRecordingRateTracker
    {
        private const double _bytesPerMegabyte = 1024 * 1024;

        private readonly TimeSpan _window = TimeSpan.FromMinutes(5);
        private readonly DateTime _startedAtUtc = DateTime.UtcNow;
        private readonly object _lock = new();
        private readonly Queue<RecordedVideoChunkDto> _recordedChunks = new();

        public void RecordChunk(Guid deviceId, long sizeInBytes)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;

                _recordedChunks.Enqueue(new RecordedVideoChunkDto
                {
                    RecordedAtUtc = now,
                    DeviceId = deviceId,
                    SizeInBytes = sizeInBytes
                });

                RemoveExpiredChunks(now);
            }
        }

        public double GetBytesPerSecond()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;

                RemoveExpiredChunks(now);

                return _recordedChunks.Sum(x => x.SizeInBytes) / GetMeasuredSeconds(now);
            }
        }

        public List<CameraRecordingRateDto> GetCameraRecordingRates()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;

                RemoveExpiredChunks(now);

                var measuredSeconds = GetMeasuredSeconds(now);

                return _recordedChunks
                    .GroupBy(x => x.DeviceId)
                    .Select(x => new CameraRecordingRateDto
                    {
                        DeviceId = x.Key,
                        MegabytesPerSecond = x.Sum(y => y.SizeInBytes) / _bytesPerMegabyte / measuredSeconds
                    })
                    .OrderByDescending(x => x.MegabytesPerSecond)
                    .ToList();
            }
        }

        private void RemoveExpiredChunks(DateTime now)
        {
            while (_recordedChunks.Count > 0 && now - _recordedChunks.Peek().RecordedAtUtc > _window)
            {
                _recordedChunks.Dequeue();
            }
        }

        private double GetMeasuredSeconds(DateTime now)
        {
            return Math.Max(1, Math.Min(_window.TotalSeconds, (now - _startedAtUtc).TotalSeconds));
        }
    }
}
