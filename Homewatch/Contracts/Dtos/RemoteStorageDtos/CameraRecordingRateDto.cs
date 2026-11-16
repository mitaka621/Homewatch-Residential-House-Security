namespace Homewatch.Contracts.Dtos.RemoteStorageDtos
{
    public class CameraRecordingRateDto
    {
        public Guid DeviceId { get; init; }

        public double MegabytesPerSecond { get; init; }
    }
}
