using Homewatch.Contracts.Constants;

namespace Homewatch.Contracts.Dtos.SmokeAlarmDetectorDtos
{
    public class SmokeAlarmConfigurationDto
    {
        public Guid DeviceId { get; set; }

        public string DeviceName { get; set; } = null!;

        public double MinBatterySOCForAlert { get; set; } = SmokeAlarmConfigurationDefaults.MinBatterySOCForAlert;

        public double ChargeSenseVoltageThreashold { get; set; } = SmokeAlarmConfigurationDefaults.ChargeSenseVoltageThreashold;

        public double MaxVoltageOverchargeWarning { get; set; } = SmokeAlarmConfigurationDefaults.MaxVoltageOverchargeWarning;
    }
}
