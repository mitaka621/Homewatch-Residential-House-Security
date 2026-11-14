using Homewatch.Contracts.Constants;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Homewatch.Infrastructure.Data.Entities
{
    public class SmokeAlarmDetectorConfiguration
    {

        [Key]
        public Guid DeviceId { get; set; }
        [ForeignKey(nameof(DeviceId))]
        public Device Device { get; set; } = null!;

        public double MinBatterySOCForAlert { get; set; } = SmokeAlarmConfigurationDefaults.MinBatterySOCForAlert;

        public double ChargeSenseVoltageThreashold { get; set; } = SmokeAlarmConfigurationDefaults.ChargeSenseVoltageThreashold;

        public double MaxVoltageOverchargeWarning { get; set; } = SmokeAlarmConfigurationDefaults.MaxVoltageOverchargeWarning;
    }
}
