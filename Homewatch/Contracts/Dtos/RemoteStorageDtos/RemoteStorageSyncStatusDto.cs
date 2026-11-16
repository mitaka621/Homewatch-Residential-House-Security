using Homewatch.Contracts.Enums;

namespace Homewatch.Contracts.Dtos.RemoteStorageDtos
{
    public class RemoteStorageSyncStatusDto
    {
        public Guid TargetId { get; init; }

        public RemoteStorageSyncState State { get; init; }

        public int ParallelCopies { get; init; }

        public double LoadPercent { get; init; }

        public double RecordingMegabytesPerSecond { get; init; }

        public double UploadMegabytesPerSecond { get; init; }

        public int PendingChunkCount { get; init; }

        public TimeSpan? DelayAfterRecording { get; init; }

        public TimeSpan? CatchUpTimeRemaining { get; init; }
    }
}
