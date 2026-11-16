namespace Homewatch.Contracts.Abstractions.Services
{
    public interface IRemoteStorageSyncController
    {
        void RequestRefresh();

        Task StopSyncAsync(Guid targetId);
    }
}
