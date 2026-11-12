using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NullWave.Models;
using NullWave.Services.SmartSorting;

namespace NullWave.Services;

public class EffectsTierResolver : INotifyPropertyChanged
{
    private static readonly Lazy<EffectsTierResolver> _shared = new(() => new EffectsTierResolver(PreferencesService.Shared));
    public static EffectsTierResolver Shared => _shared.Value;

    private readonly PreferencesService _prefs;
    private readonly Func<HardwareInfo?>? _hardwareProbe;
    private readonly Action<Action>? _post;
    private EffectsTier? _devOverride;
    private EffectsTier? _batteryOverride;
    private bool _resolutionLogged;

    public EffectsTierResolver(PreferencesService prefs)
        : this(prefs, null, null) { }

    public EffectsTierResolver(PreferencesService prefs, Func<HardwareInfo?>? hardwareProbe, Action<Action>? post)
    {
        _prefs = prefs;
        _hardwareProbe = hardwareProbe;
        _post = post;
    }

    public void SetDevOverride(EffectsTier? tier) 
    { 
        _devOverride = tier; 
        OnPropertyChanged(nameof(Current)); 
    }
    
    public void SetBatteryOverride(EffectsTier tier) 
    { 
        _batteryOverride = tier; 
        OnPropertyChanged(nameof(Current)); 
    }

    public void ClearBatteryOverride() 
    { 
        _batteryOverride = null; 
        OnPropertyChanged(nameof(Current)); 
    }

    public void NotifyPreferencesChanged() => OnPropertyChanged(nameof(Current));

    public EffectsTier Current
    {
        get
        {
            EffectsTier result;
            
            // 1. Dev override always wins (for testing in Dev Tab)
            if (_devOverride.HasValue)
            {
                result = _devOverride.Value;
            }
            // 2. Battery override forces Minimal to save power/reduce heat
            else if (_batteryOverride.HasValue && _prefs.Current.AutoEffectsTier)
            {
                result = _batteryOverride.Value;
            }
            // 3. Auto-detect based on hardware
            else if (_prefs.Current.AutoEffectsTier)
            {
                var info = _hardwareProbe != null ? _hardwareProbe() : HardwareDetector.CachedOrNull();
                result = info != null && (info.HasNvidia || info.HasAmd || info.HasModernGpu)
                    ? EffectsTier.Standard
                    : EffectsTier.Minimal;
            }
            // 4. Manual override from Settings
            else
            {
                result = System.Enum.TryParse<EffectsTier>(_prefs.Current.EffectsTier, out var manual)
                    ? manual
                    : EffectsTier.Standard;
            }

            // Log once when we have complete hardware info (not "pending")
            if (!_resolutionLogged && Serilog.Log.IsEnabled(Serilog.Events.LogEventLevel.Information))
            {
                var info = _hardwareProbe != null ? _hardwareProbe() : HardwareDetector.CachedOrNull();
                
                // Only log when detection is complete (not pending)
                if (info != null || !_prefs.Current.AutoEffectsTier)
                {
                    _resolutionLogged = true;
                    Serilog.Log.Information(
                        "[EFFECTS] tier={Tier} auto={Auto} dev={DevOverride} battery={BatteryOverride} nvidia={Nvidia} amd={Amd} modern={Modern} gpu={GpuName}",
                        result,
                        _prefs.Current.AutoEffectsTier,
                        _devOverride.HasValue,
                        _batteryOverride.HasValue,
                        info?.HasNvidia ?? false,
                        info?.HasAmd ?? false,
                        info?.HasModernGpu ?? false,
                        info?.GpuName ?? "none");
                }
            }
            return result;
        }
    }

    public Task AttachDetection(Task<HardwareInfo> detection)
    {
        return detection.ContinueWith(task =>
        {
            if (!task.IsCompletedSuccessfully) return;
            Action notify = () => OnPropertyChanged(nameof(Current));
            if (_post != null) _post(notify);
            else notify();
        }, TaskScheduler.Default);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}