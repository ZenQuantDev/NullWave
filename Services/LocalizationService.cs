using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using NullWave.Helpers.Localization;

namespace NullWave.Services;

public class LocalizationService : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationService> _instance = new(() => new LocalizationService());
    public static LocalizationService Instance => _instance.Value;

    public event PropertyChangedEventHandler? PropertyChanged;

    private string _currentLanguage = "en-US";
    private Dictionary<string, string> _activeDictionary = Locales.English;
    private int _version; // Bump on every language change to force binding re-evaluation

    public static List<KeyValuePair<string, string>> SupportedLanguages { get; } =
        Locales.Available.Select(l => new KeyValuePair<string, string>(l.Code, l.DisplayName)).ToList();

    public string CurrentLanguage => _currentLanguage;
    public int Version => _version;

    public string this[string key]
    {
        get
        {
            if (_activeDictionary.TryGetValue(key, out var val))
                return val;
            if (Locales.English.TryGetValue(key, out var englishVal))
                return englishVal;
            return $"[{key}]";
        }
    }

    public void Initialize(string languageCode)
    {
        SetLanguage(languageCode);
    }

    public void SetLanguage(string languageCode)
    {
        var normalized = string.IsNullOrWhiteSpace(languageCode) ? "en-US" : languageCode;
        if (_currentLanguage == normalized && _version > 0) return;

        _currentLanguage = normalized;
        _activeDictionary = Locales.GetDictionary(_currentLanguage);
        _version++;
        Serilog.Log.Information("[Locale] SetLanguage {Code} v{Version}", normalized, _version);

        // Notify ALL indexer bindings. Avalonia re-evaluates [Key] paths on "Item[]" notification.
        // We also fire a named property so any binding to Version (if used) updates too.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Version)));
    }
}