using Homewatch.Contracts.Constants;
using Homewatch.Contracts.Enums;
using SixLabors.ImageSharp.Processing;

namespace Homewatch.Contracts.Dtos.CameraConfigurationDtos
{
    public class CameraStreamingConfigurationDto
    {
        public Guid DeviceId { get; set; }

        public float FrameRotation { get; set; } = CameraConfigurationDefaults.FrameRotation;

        public float ZoomFactor { get; set; } = CameraConfigurationDefaults.ZoomFactor;

        public int ZoomStartX { get; set; } = CameraConfigurationDefaults.ZoomStartX;

        public int ZoomStartY { get; set; } = CameraConfigurationDefaults.ZoomStartY;

        public float Brightness { get; set; } = CameraConfigurationDefaults.Brightness;

        public float Contrast { get; set; } = CameraConfigurationDefaults.Contrast;

        public FlipMode FlipMode { get; set; } = CameraConfigurationDefaults.FlipMode;

        public CameraAspectRatios CameraAspectRatio { get; set; } = CameraConfigurationDefaults.CameraAspectRatio;

        public CameraSensorResolutions SensorResolution { get; set; } = CameraConfigurationDefaults.SensorResolution;
    }
}
