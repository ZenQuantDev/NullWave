using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using NullWave.Services;

namespace NullWave.Helpers.Localization;

/// <summary>
/// Usage: Text="{loc:Loc Some_Key}"
/// Returns a live one-way binding to LocalizationService.Instance[Key],
/// so every usage re-renders instantly when the language changes.
/// </summary>
public class LocExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public LocExtension() { }

    public LocExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // IMPORTANT: Avalonia path grammar treats "[Key]" as an indexer on the SOURCE.
        // Do NOT use "Item[Key]" - "Item" is not a parameterless property, so the
        // first path step fails and every bound Text renders empty.
        return new Binding
        {
            Source = LocalizationService.Instance,
            Path = $"[{Key}]",
            Mode = BindingMode.OneWay
        };
    }
}