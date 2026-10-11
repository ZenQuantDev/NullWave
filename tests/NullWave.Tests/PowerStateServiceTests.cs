using System.IO;
using Xunit;
using NullWave.Services.SmartSorting;

namespace NullWave.Tests;

public class PowerStateServiceTests
{
    [Fact]
    public void ParseSysFs_ReturnsBattery_WhenDischarging()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "sysfs_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var batDir = Path.Combine(tempDir, "BAT0");
            Directory.CreateDirectory(batDir);
            File.WriteAllText(Path.Combine(batDir, "type"), "Battery\n");
            File.WriteAllText(Path.Combine(batDir, "status"), "Discharging\n");

            var state = PowerStateService.ParseSysFs(tempDir);
            Assert.Equal(PowerState.Battery, state);
        }
        finally { Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void ParseSysFs_IgnoresDeviceScope_Batteries()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "sysfs_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            // Mouse battery (Should be ignored)
            var mouseDir = Path.Combine(tempDir, "hidpp_battery_0");
            Directory.CreateDirectory(mouseDir);
            File.WriteAllText(Path.Combine(mouseDir, "type"), "Battery\n");
            File.WriteAllText(Path.Combine(mouseDir, "scope"), "Device\n");
            File.WriteAllText(Path.Combine(mouseDir, "status"), "Discharging\n");

            // Mains (Plugged in)
            var acDir = Path.Combine(tempDir, "AC");
            Directory.CreateDirectory(acDir);
            File.WriteAllText(Path.Combine(acDir, "type"), "Mains\n");
            File.WriteAllText(Path.Combine(acDir, "online"), "1\n");

            var state = PowerStateService.ParseSysFs(tempDir);
            Assert.Equal(PowerState.AC, state); // Proves the mouse battery didn't trigger "Battery" mode
        }
        finally { Directory.Delete(tempDir, true); }
    }
}