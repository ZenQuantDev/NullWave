using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NullWave.Models;
using NullWave.Services.SmartSorting;

namespace NullWave.Services;

public class EffectsTierResolver : INotifyPropertyChanged
{
    private readonly PreferencesService _prefs;
    private readonly Func<HardwareInfo?>? _hardwareProbe;
    private readonly Action<Action>? _post;
    private EffectsTier? _devOverride;
    private EffectsTier? _batteryOverride;

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

    /// <summary>
    /// Call this when AutoEffectsTier or manual EffectsTier preferences change in the Settings UI.
    /// </summary>
    public void NotifyPreferencesChanged() => OnPropertyChanged(nameof(Current));

    public EffectsTier Current
    {
        get
        {
            // 1. Dev override always wins (for testing in Dev Tab)
            if (_devOverride.HasValue) return _devOverride.Value;

            // 2. Battery override forces Minimal to save power/reduce heat
            if (_batteryOverride.HasValue && _prefs.Current.AutoEffectsTier) 
                return _batteryOverride.Value;

            // 3. Auto-detect based on hardware
            if (_prefs.Current.AutoEffectsTier)
            {
                var info = _hardwareProbe != null ? _hardwareProbe() : HardwareDetector.CachedOrNull();
                return info != null && (info.HasNvidia || info.HasAmd)
                    ? EffectsTier.Standard
                    : EffectsTier.Minimal;
            }

            // 4. Manual override from Settings
            return System.Enum.TryParse<EffectsTier>(_prefs.Current.EffectsTier, out var manual)
                ? manual
                : EffectsTier.Standard;
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