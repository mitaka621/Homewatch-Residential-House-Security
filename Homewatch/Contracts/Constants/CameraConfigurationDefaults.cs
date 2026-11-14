using Homewatch.Contracts.Enums;

namespace Homewatch.Contracts.Constants
{
    public static class CameraConfigurationDefaults
    {
        public const float FrameRotation = 0;

        public const float ZoomFactor = 1;

        public const int ZoomStartX = 0;

        public const int ZoomStartY = 0;

        public const float Brightness = 1;

        public const float Contrast = 1;

        public const FlipMode FlipMode = FlipMode.None;

        public const CameraAspectRatios CameraAspectRatio = CameraAspectRatios.Original;

        public const CameraSensorResolutions SensorResolution = CameraSensorResolutions.Resolution2048x1536;

        public const bool CaseSensorInstalled = true;
    }
}
