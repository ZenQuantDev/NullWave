using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace NullWave.Services.SmartSorting;

public enum PowerState { AC, Battery, Unknown }

/// <summary>
/// Lightweight service that reads the system power state (AC/battery)
/// and raises an event when it changes, so LocalAIService can auto-switch
/// models without polling from the UI layer.
/// </summary>
public class PowerStateService : IDisposable
{
    private PowerState _current = PowerState.Unknown;
    private Timer? _pollTimer;
    private bool _disposed;

    public PowerState Current => _current;
    public event Action<PowerState>? PowerStateChanged;

    public PowerStateService()
    {
        _current = ReadPowerState();
    }

    /// <summary>
    /// Start polling every 15 seconds. Cheap - just reads a sysfs file.
    /// </summary>
    public void StartPolling()
    {
        _pollTimer = new Timer(_ => CheckAndNotify(), null,
            dueTime: TimeSpan.FromSeconds(15),
            period:  TimeSpan.FromSeconds(15));
    }

    public void StopPolling()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    private void CheckAndNotify()
    {
        var state = ReadPowerState();
        if (state != _current)
        {
            _current = state;
            Log.Information("[PowerState] Power state changed to {State}", state);
            PowerStateChanged?.Invoke(state);
        }
    }

    public static PowerState ReadPowerState()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // /sys/class/power_supply/AC/online → "1" = plugged, "0" = battery
                // Some systems use "AC0" or "ACAD" - try common names
                foreach (var name in new[] { "AC", "AC0", "ACAD", "ADP0", "ADP1" })
                {
                    var path = $"/sys/class/power_supply/{name}/online";
                    if (!File.Exists(path)) continue;

                    var val = File.ReadAllText(path).Trim();
                    return val == "1" ? PowerState.AC : PowerState.Battery;
                }

                // Fallback: check if any battery is discharging
                var supplyDir = "/sys/class/power_supply";
                if (Directory.Exists(supplyDir))
                {
                    foreach (var dir in Directory.GetDirectories(supplyDir))
                    {
                        var statusPath = Path.Combine(dir, "status");
                        if (!File.Exists(statusPath)) continue;
                        var status = File.ReadAllText(statusPath).Trim();
                        if (status == "Discharging") return PowerState.Battery;
                        if (status is "Charging" or "Full") return PowerState.AC;
                    }
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // System.Windows.Forms.SystemInformation isn't available in WinExe without Forms,
                // so we use the Win32 GetSystemPowerStatus via P/Invoke
                if (GetSystemPowerStatus(out var status))
                    return status.ACLineStatus == 1 ? PowerState.AC : PowerState.Battery;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[PowerState] Could not read power state");
        }

        return PowerState.Unknown;
    }

    // ── Win32 P/Invoke for Windows power state ────────────────────────────
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;       // 0 = offline, 1 = online, 255 = unknown
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopPolling();
        _disposed = true;
    }
}