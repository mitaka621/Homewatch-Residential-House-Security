using Homewatch.Contracts.Constants;
using Homewatch.Contracts.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Homewatch.Infrastructure.Data.Entities
{
    public class CameraConfiguration
    {
        [Key]
        public Guid DeviceId { get; set; }
        [ForeignKey(nameof(DeviceId))]
        public Device Device { get; set; } = null!;

        public float FrameRotation { get; set; } = CameraConfigurationDefaults.FrameRotation;

        public float ZoomFactor { get; set; } = CameraConfigurationDefaults.ZoomFactor;

        public int ZoomStartX { get; set; } = CameraConfigurationDefaults.ZoomStartX;

        public int ZoomStartY { get; set; } = CameraConfigurationDefaults.ZoomStartY;

        public float Brightness { get; set; } = CameraConfigurationDefaults.Brightness;

        public float Contrast { get; set; } = CameraConfigurationDefaults.Contrast;

        public FlipMode FlipMode { get; set; } = CameraConfigurationDefaults.FlipMode;

        public CameraAspectRatios CameraAspectRatio { get; set; } = CameraConfigurationDefaults.CameraAspectRatio;

        public CameraSensorResolutions SensorResolution { get; set; } = CameraConfigurationDefaults.SensorResolution;

        public bool SecurityArmed { get; set; }

        public bool CaseSensorInstalled { get; set; } = CameraConfigurationDefaults.CaseSensorInstalled;

        public double MovementThresholdOffset { get; set; }

        public double RotationThresholdOffset { get; set; }
    }
}
