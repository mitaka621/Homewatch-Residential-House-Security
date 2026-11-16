namespace Homewatch.Contracts.Dtos.RemoteStorageDtos
{
    public class RecordedVideoChunkDto
    {
        public DateTime RecordedAtUtc { get; init; }

        public Guid DeviceId { get; init; }

        public long SizeInBytes { get; init; }
    }
}
