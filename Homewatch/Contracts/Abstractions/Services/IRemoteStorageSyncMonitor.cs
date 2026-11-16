using Homewatch.Contracts.Dtos.RemoteStorageDtos;

namespace Homewatch.Contracts.Abstractions.Services
{
    public interface IRemoteStorageSyncMonitor
    {
        RemoteStorageSyncStatusDto? GetSyncStatus(Guid targetId);

        List<CameraRecordingRateDto> GetCameraRecordingRates();
    }
}
