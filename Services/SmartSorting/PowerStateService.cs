using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Serilog;

namespace NullWave.Services.SmartSorting;

public enum PowerState { AC, Battery, Unknown }

public class PowerStateService : IDisposable
{
    private PowerState _current = PowerState.Unknown;
    private ITimer? _pollTimer; 
    private bool _disposed;
    private readonly TimeProvider _timeProvider;
    private readonly string _sysFsRoot;

    public PowerState Current => _current;
    public event Action<PowerState>? PowerStateChanged;

    public const string DefaultSysFsRoot = "/sys/class/power_supply";

    public PowerStateService(TimeProvider? timeProvider = null, string sysFsRoot = DefaultSysFsRoot)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _sysFsRoot = sysFsRoot;
        _current = ReadPowerState(_sysFsRoot);
    }

    public void StartPolling()
    {
        _pollTimer = _timeProvider.CreateTimer(_ => CheckAndNotify(), null,
            TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
    }

    public void StopPolling()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    private void CheckAndNotify()
    {
        var state = ReadPowerState(_sysFsRoot);
        if (state != _current)
        {
            _current = state;
            Log.Information("[PowerState] Power state changed to {State}", state);
            PowerStateChanged?.Invoke(state);
        }
    }

    public static PowerState ReadPowerState(string sysFsRoot = DefaultSysFsRoot)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return ParseSysFs(sysFsRoot);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (GetSystemPowerStatus(out var status))
                {
                    return status.ACLineStatus switch
                    {
                        0 => PowerState.Battery,
                        1 => PowerState.AC,
                        255 => PowerState.Unknown,
                        _ => PowerState.Unknown
                    };
                }
            }
            else
            {
                // Fallback for macOS or testing sysfs on Windows
                if (Directory.Exists(sysFsRoot)) return ParseSysFs(sysFsRoot);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[PowerState] Could not read power state");
        }
        return PowerState.Unknown;
    }

    public static PowerState ParseSysFs(string sysFsRoot)
    {
        if (!Directory.Exists(sysFsRoot)) return PowerState.Unknown;

        bool hasBattery = false;
        bool isDischarging = false;
        bool isAcOnline = false;

        foreach (var dir in Directory.GetDirectories(sysFsRoot))
        {
            var typePath = Path.Combine(dir, "type");
            var scopePath = Path.Combine(dir, "scope");
            var statusPath = Path.Combine(dir, "status");
            var onlinePath = Path.Combine(dir, "online");

            string type = File.Exists(typePath) ? File.ReadAllText(typePath).Trim() : "";
            string scope = File.Exists(scopePath) ? File.ReadAllText(scopePath).Trim() : "";
            
            if (scope.Equals("Device", StringComparison.OrdinalIgnoreCase)) continue;
            if (type.Equals("USB", StringComparison.OrdinalIgnoreCase)) continue;

            if (type.Equals("Battery", StringComparison.OrdinalIgnoreCase))
            {
                hasBattery = true;
                if (File.Exists(statusPath))
                {
                    var status = File.ReadAllText(statusPath).Trim();
                    if (status.Equals("Discharging", StringComparison.OrdinalIgnoreCase)) isDischarging = true;
                }
            }
            else if (type.Equals("Mains", StringComparison.OrdinalIgnoreCase) || 
                     type.Equals("AC", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(onlinePath))
                {
                    var online = File.ReadAllText(onlinePath).Trim();
                    if (online == "1") isAcOnline = true;
                }
            }
        }

        if (isDischarging) return PowerState.Battery;
        if (hasBattery && !isDischarging) return PowerState.AC; 
        if (isAcOnline) return PowerState.AC;
        
        return PowerState.Unknown;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;       
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