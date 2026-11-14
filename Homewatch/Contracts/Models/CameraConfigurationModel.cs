using Homewatch.Contracts.Constants;
using Homewatch.Contracts.Enums;
using MudBlazor;
using System.ComponentModel.DataAnnotations;

namespace Homewatch.Contracts.Models
{
    public class CameraConfigurationModel
    {

        [StringLength(100, MinimumLength = 1, ErrorMessage = "Camera name must be between 1 and 100 characters.")]
        [Label("Camera Name")]
        public required string CameraName { get; set; } = string.Empty;

        public Guid DeviceId { get; set; }

        [Range(-180, 180, ErrorMessage = "Frame rotation must be between -180 and 180 degrees.")]
        public float FrameRotation { get; set; } = CameraConfigurationDefaults.FrameRotation;

        [Range(1, 2, ErrorMessage = "Zoom factor must be between 1 and 2.")]
        public float ZoomFactor { get; set; } = CameraConfigurationDefaults.ZoomFactor;

        [Range(0, int.MaxValue, ErrorMessage = "Zoom start X must be a non-negative integer.")]
        public int ZoomStartX { get; set; } = CameraConfigurationDefaults.ZoomStartX;

        [Range(0, int.MaxValue, ErrorMessage = "Zoom start Y must be a non-negative integer.")]
        public int ZoomStartY { get; set; } = CameraConfigurationDefaults.ZoomStartY;

        [Range(0, 2, ErrorMessage = "Brightness must be between 0 and 2.")]
        public float Brightness { get; set; } = CameraConfigurationDefaults.Brightness;

        [Range(0, 2, ErrorMessage = "Contrast must be between 0 and 2.")]
        public float Contrast { get; set; } = CameraConfigurationDefaults.Contrast;

        public FlipMode FlipMode { get; set; } = CameraConfigurationDefaults.FlipMode;

        public CameraAspectRatios CameraAspectRatio { get; set; } = CameraConfigurationDefaults.CameraAspectRatio;

        [EnumDataType(typeof(CameraSensorResolutions), ErrorMessage = "Unsupported camera sensor resolution.")]
        public CameraSensorResolutions SensorResolution { get; set; } = CameraConfigurationDefaults.SensorResolution;
    }
}
