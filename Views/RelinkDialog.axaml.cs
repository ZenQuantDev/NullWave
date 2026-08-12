using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace NullWave.Views;

/// <summary>Return value from RelinkDialog. One or both fields may be non-null.</summary>
public struct RelinkResult
{
    public string? Url;
    public string? Path;
    public bool Cancelled => Url == null && Path == null;
}

public partial class RelinkDialog : Window
{
    private string? _chosenPath;

    // Required for Avalonia XAML loader
    public RelinkDialog() : this("") { }

    public RelinkDialog(string initial = "")
    {
        InitializeComponent();
        UrlInput.Text = initial ?? "";
        UrlInput.AttachedToVisualTree += (_, _) => UrlInput.Focus();
    }

    private async void OnChooseFile(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose audio file",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Audio") { Patterns = new[] { "*.mp3", "*.flac", "*.ogg", "*.m4a", "*.wav", "*.aac" } } }
        });
        if (files.Count == 0) return;
        _chosenPath = files[0].Path.LocalPath;
        UrlInput.Text = _chosenPath;
    }

    private void OnSave(object? sender, RoutedEventArgs e) =>
        Close(new RelinkResult { Url = _chosenPath == null ? UrlInput.Text?.Trim() : null, Path = _chosenPath });

    private void OnCancel(object? sender, RoutedEventArgs e) =>
        Close(new RelinkResult { Url = null, Path = null });
}