using Homewatch.Contracts.Enums;

namespace Homewatch.Contracts.Constants
{
    public static class SystemSettingsDefaults
    {
        public const RetentionPeriod EncodedVideoRetention = RetentionPeriod.OneMonth;

        public const RetentionPeriod CameraChunkRetention = RetentionPeriod.OneWeek;

        public const RetentionPeriod DismissedNotificationRetention = RetentionPeriod.OneYear;

        public const int SecurityMinFps = 4;

        public const double SecurityMaxTemperatureC = 90;

        public const double SecurityMaxHumidityPercent = 70;
    }
}
