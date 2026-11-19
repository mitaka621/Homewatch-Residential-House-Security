using Homewatch.Contracts.Abstractions.Repositories;
using Homewatch.Contracts.Abstractions.Services;
using Homewatch.Contracts.Constants;
using Homewatch.Contracts.Dtos.NotificationDtos;
using Homewatch.Contracts.Dtos.SmokeAlarmDetectorDtos;
using Homewatch.Contracts.Enums;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace Homewatch.Core.Services.Devices
{
    public class SmokeAlarmDetectorManagerService : ISmokeAlarmDetectorManagerService
    {
        private readonly ISmokeAlarmTelemetryRepository _smokeAlarmDetectorRepository;
        private readonly ILogger<SmokeAlarmDetectorManagerService> _logger;
        private readonly INotificationsService _notificationService;

        public event Action<SmokeAlarmDetectorPayloadDto>? OnNewTelemetryAdded;

        public SmokeAlarmDetectorManagerService(
            ISmokeAlarmTelemetryRepository smokeAlarmDetectorRepository,
            ILogger<SmokeAlarmDetectorManagerService> logger,
            INotificationsService notificationService)
        {
            _smokeAlarmDetectorRepository = smokeAlarmDetectorRepository;
            _logger = logger;
            _notificationService = notificationService;
        }

        public async Task IngestAsync(SmokeAlarmDetectorPayloadDto payload, SmokeAlarmConfigurationDto config)
        {
            var previousTelemetry = await _smokeAlarmDetectorRepository.GetLatestTelemetryForDetectorAsync(payload.DeviceId);

            if (!await _smokeAlarmDetectorRepository.SavePayloadAsync(payload))
            {
                _logger.LogError("Failed to save payload for device {DeviceId}: {Payload}", payload.DeviceId, payload);
                return;
            }

            var handlers = OnNewTelemetryAdded;
            if (handlers != null)
            {
                foreach (Action<SmokeAlarmDetectorPayloadDto> handler in handlers.GetInvocationList())
                {
                    var localHandler = handler;
                    _ = Task.Run(async () =>
                    {
                        try { localHandler(payload); }
                        catch (Exception ex) { _logger.LogError(ex, "Smoke alarm handler failed for device {DeviceId}", payload.DeviceId); }
                    });
                }
            }

            if (payload.Event == SmokeAlarmEvent.AlarmLowBatteryChirp)
            {
                _ = _notificationService.PushNotificationToAllUsersAsync(new CreateNotificationDto()
                {
                    Name = $"Smoke alarm low battery chirp detected - {config.DeviceName}",
                    Description = $"Change the battery of the smoke detector",
                    Icon = Icons.Material.Filled.BatterySaver,
                    IconColor = Color.Info,
                    IsUnique = true,
                    NotificationType = NotificationType.System,
                    RelativeRedirectUrl = AppRoutes.DeviceConfig(config.DeviceId)
                });
            }

            if (payload.Event == SmokeAlarmEvent.FireAlarm)
            {
                _ = _notificationService.PushNotificationToAllUsersAsync(new CreateNotificationDto()
                {
                    Name = $"FIRE DETECTED - {config.DeviceName}",
                    Description = $"The smoke alarm activated",
                    Icon = Icons.Material.Filled.LocalFireDepartment,
                    IconColor = Color.Error,
                    IsUnique = true,
                    NotificationType = NotificationType.EnvironmentalAlert,
                    RelativeRedirectUrl = AppRoutes.DeviceConfig(config.DeviceId)
                });
            }

            if (previousTelemetry?.Event == SmokeAlarmEvent.FireAlarm && payload.Event != SmokeAlarmEvent.FireAlarm)
            {
                _ = _notificationService.PushNotificationToAllUsersAsync(new CreateNotificationDto()
                {
                    Name = $"Fire alarm cleared - {config.DeviceName}",
                    Description = "The smoke alarm stopped sounding",
                    Icon = Icons.Material.Filled.FireExtinguisher,
                    IconColor = Color.Success,
                    IsUnique = true,
                    NotificationType = NotificationType.EnvironmentalAlert,
                    RelativeRedirectUrl = AppRoutes.DeviceConfig(config.DeviceId)
                });
            }

            if (!payload.IsCharging)
            {
                if (payload.BatterySOCPercent > config.MinBatterySOCForAlert)
                {
                    return;
                }

                _ = _notificationService.PushNotificationToAllUsersAsync(new CreateNotificationDto()
                {
                    Name = $"Smoke alarm detector low battery. Charge soon - {config.DeviceName}.",
                    Description = $"The smoke alarm detector's battery state of charge is {payload.BatterySOCPercent}%",
                    Icon = Icons.Material.Filled.Battery0Bar,
                    IconColor = Color.Warning,
                    IsUnique = false,
                    NotificationType = NotificationType.System,
                    RelativeRedirectUrl = AppRoutes.DeviceConfig(config.DeviceId)
                });

                return;
            }

            if (payload.BatteryVoltage > config.MaxVoltageOverchargeWarning)
            {
                _ = _notificationService.PushNotificationToAllUsersAsync(new CreateNotificationDto()
                {
                    Name = $"Overcharge warning for {config.DeviceName}",
                    Description = $"IMMEDIATELY disconnect the device from the charger. Risk of fire and explosion. Battery cell coltage {payload.BatteryVoltage}V",
                    Icon = Icons.Material.Filled.BatteryAlert,
                    IconColor = Color.Error,
                    NotificationType = NotificationType.System,
                    RelativeRedirectUrl = AppRoutes.DeviceConfig(config.DeviceId),
                    IsUnique = true,
                });

                return;
            }

            if (payload.BatterySOCPercent < 100)
            {
                _ = _notificationService.PushNotificationToAllUsersAsync(new CreateNotificationDto()
                {
                    Name = $"Smoke alarm {config.DeviceName} is charging.",
                    Description = "Battery level and device status will be synced with the server every 30 min.",
                    Icon = Icons.Material.Filled.BatteryChargingFull,
                    IconColor = Color.Info,
                    IsUnique = true,
                    NotificationType = NotificationType.System,
                    RelativeRedirectUrl = AppRoutes.DeviceConfig(config.DeviceId)
                });

                return;
            }

            _ = _notificationService.PushNotificationToAllUsersAsync(new CreateNotificationDto()
            {
                Name = $"Battery Charged - {config.DeviceName}",
                Description = "Battery level state of charge is 100%",
                Icon = Icons.Material.Filled.Battery6Bar,
                IconColor = Color.Success,
                IsUnique = true,
                NotificationType = NotificationType.System,
                RelativeRedirectUrl = AppRoutes.DeviceConfig(config.DeviceId)
            });
        }

        public Task<SmokeAlarmDetectorPayloadDto?> GetLatestTelemetryAsync(Guid deviceId)
        {
            return _smokeAlarmDetectorRepository.GetLatestTelemetryForDetectorAsync(deviceId);
        }
    }
}
