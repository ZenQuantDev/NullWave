using System.ComponentModel;
using System.Runtime.CompilerServices;
using NullWave.Models;
using NullWave.Services.SmartSorting;

namespace NullWave.Services;

public class EffectsTierResolver : INotifyPropertyChanged
{
    private readonly PreferencesService _prefs;
    private EffectsTier? _devOverride;
    private EffectsTier? _batteryOverride;

    public EffectsTierResolver(PreferencesService prefs) => _prefs = prefs;

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
                // NOTE: We intentionally never auto-select 'Full' yet, because no 
                // Full-tier features (glow, visualizer) are implemented. 
                // Auto only chooses between Minimal (integrated/old) and Standard (capable).
                return HardwareDetector.SupportsFullEffectsTier() 
                    ? EffectsTier.Standard 
                    : EffectsTier.Minimal;
            }

            // 4. Manual override from Settings
            return System.Enum.TryParse<EffectsTier>(_prefs.Current.EffectsTier, out var manual)
                ? manual
                : EffectsTier.Standard;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}