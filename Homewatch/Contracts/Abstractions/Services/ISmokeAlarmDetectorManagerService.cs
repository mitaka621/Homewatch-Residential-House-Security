using Homewatch.Contracts.Dtos.SmokeAlarmDetectorDtos;

namespace Homewatch.Contracts.Abstractions.Services
{
    public interface ISmokeAlarmDetectorManagerService
    {
        public event Action<SmokeAlarmDetectorPayloadDto>? OnNewTelemetryAdded;

        Task IngestAsync(SmokeAlarmDetectorPayloadDto payload, SmokeAlarmConfigurationDto config);

        Task<SmokeAlarmDetectorPayloadDto?> GetLatestTelemetryAsync(Guid deviceId);
    }
}
