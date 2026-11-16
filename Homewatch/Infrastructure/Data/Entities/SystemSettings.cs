using Homewatch.Contracts.Constants;
using Homewatch.Contracts.Enums;
using System.ComponentModel.DataAnnotations;

namespace Homewatch.Infrastructure.Data.Entities
{
    public class SystemSettings
    {
        [Key]
        public Guid Id { get; set; }

        public RetentionPeriod EncodedVideoRetention { get; set; } = SystemSettingsDefaults.EncodedVideoRetention;

        public RetentionPeriod CameraChunkRetention { get; set; } = SystemSettingsDefaults.CameraChunkRetention;

        public int SecurityMinFps { get; set; } = SystemSettingsDefaults.SecurityMinFps;

        public double SecurityMaxTemperatureC { get; set; } = SystemSettingsDefaults.SecurityMaxTemperatureC;

        public double SecurityMaxHumidityPercent { get; set; } = SystemSettingsDefaults.SecurityMaxHumidityPercent;

        public RetentionPeriod DismissedNotificationRetention { get; set; } = SystemSettingsDefaults.DismissedNotificationRetention;

        public int ParallelCopiesPerShare { get; set; } = SystemSettingsDefaults.ParallelCopiesPerShare;
    }
}
