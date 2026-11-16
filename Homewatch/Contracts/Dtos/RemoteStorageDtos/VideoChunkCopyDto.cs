namespace Homewatch.Contracts.Dtos.RemoteStorageDtos
{
    public class VideoChunkCopyDto
    {
        public DateTime CompletedAtUtc { get; init; }

        public TimeSpan Duration { get; init; }

        public long SizeInBytes { get; init; }
    }
}
