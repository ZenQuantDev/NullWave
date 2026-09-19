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

    /// <summary>
    /// Bound by UI selection controls (e.g. ComboBox). Automatically stays updated with all defined locales.
    /// </summary>
    public static List<KeyValuePair<string, string>> SupportedLanguages =>
        Locales.Available.Select(l => new KeyValuePair<string, string>(l.Code, l.DisplayName)).ToList();

    public string CurrentLanguage => _currentLanguage;

    /// <summary>
    /// Indexer used for dynamic localized string lookup in Avalonia XAML bindings.
    /// </summary>
    public string this[string key]
    {
        get
        {
            if (_activeDictionary.TryGetValue(key, out var val))
                return val;

            // Fallback to English if missing in current active locale
            if (Locales.English.TryGetValue(key, out var englishVal))
                return englishVal;

            // Missing key placeholder
            return $"[{key}]";
        }
    }

    public void Initialize(string languageCode)
    {
        SetLanguage(languageCode);
    }

    public void SetLanguage(string languageCode)
    {
        _currentLanguage = string.IsNullOrWhiteSpace(languageCode) ? "en-US" : languageCode;
        _activeDictionary = Locales.GetDictionary(_currentLanguage);

        // Notify Avalonia UI to refresh all active bindings on the indexer
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}