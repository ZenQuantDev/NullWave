using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels;
using Serilog;

namespace NullWave.Views;

public partial class ProfileWindow : Window
{
    public ProfileWindow()
    {
        InitializeComponent();
        Loaded += OnWindowLoaded;
    }

    private async void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnWindowLoaded;
        if (DataContext is not UserProfileViewModel vm) return;
        vm.EnsureShareAssets();

        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            var text = await ClipboardTextReader.TryGetTextAsync(clipboard);
            if (string.IsNullOrWhiteSpace(text)) return;
            
            // FIX: Don't auto-import if it's a track link
            if (ShareLink.TryParseTrack(text, out _, out _)) return;

            if (!ShareLink.TryParseProfile(text, out var encoded)) return;
            var payload = ProfileShareService.Decode(encoded);
            if (payload == null) return;

            if (string.Equals(payload.Code, vm.ShareCode, StringComparison.OrdinalIgnoreCase)) return;

            vm.ImportShareString(encoded);
            ToastService.Instance.Show($"Found {payload.Name}'s profile in your clipboard - taste overlap loaded.", ToastType.Info);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[ProfileWindow] Clipboard share detection skipped");
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.E && e.KeyModifiers == KeyModifiers.Control)
        {
            ExportProfileCard();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (DataContext is UserProfileViewModel vm && vm.IsEditorOpen)
                vm.IsEditorOpen = false;
            else
                Close();
            e.Handled = true;
        }
    }

    private void OnExportProfileClicked(object? sender, RoutedEventArgs e) => ExportProfileCard();

    private async void ExportProfileCard()
    {
        if (DataContext is not UserProfileViewModel vm) return;
        vm.EnsureShareAssets();
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Profile Card",
            SuggestedFileName = $"{vm.Username}_NullWave.jpg",
            DefaultExtension = "jpg",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("JPEG Image (recommended, ~200 KB)") { Patterns = new[] { "*.jpg", "*.jpeg" } },
                new FilePickerFileType("PNG Image (lossless, larger)") { Patterns = new[] { "*.png" } }
            }
        });

        if (file == null) return;

        try
        {
            if (vm.ShareQrBitmap != null) QrImage.Source = vm.ShareQrBitmap;
            var liveSize = ProfileCard.Bounds.Size;

            ProfileCard.Measure(new Size(liveSize.Width, double.PositiveInfinity));
            ProfileCard.Arrange(new Rect(new Point(0, 0), ProfileCard.DesiredSize));
            ProfileCard.UpdateLayout();

            const double scale = 2.0;
            var pixelSize = new PixelSize(
                Math.Max(1, (int)(ProfileCard.DesiredSize.Width * scale)),
                Math.Max(1, (int)(ProfileCard.DesiredSize.Height * scale)));

            using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96 * scale, 96 * scale));
            bitmap.Render(ProfileCard);

            ProfileCard.Measure(liveSize);
            ProfileCard.Arrange(new Rect(liveSize));
            ProfileCard.InvalidateVisual();

            var ext = Path.GetExtension(file.Path.LocalPath).ToLowerInvariant();
            await using var outStream = await file.OpenWriteAsync();

            if (ext is ".jpg" or ".jpeg")
            {
                using var pngMs = new MemoryStream();
                bitmap.Save(pngMs);
                pngMs.Position = 0;
                using var sk = SkiaSharp.SKBitmap.Decode(pngMs);
                using var jpg = sk.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 92);
                jpg.SaveTo(outStream);
            }
            else
            {
                bitmap.Save(outStream);
            }

            vm.TriggerExportSuccessToast();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export profile card.");
        }
    }

    private void OnImportShareClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not UserProfileViewModel vm) return;
        var raw = ImportShareBox.Text;
        if (string.IsNullOrWhiteSpace(raw))
        {
            ToastService.Instance.Show("Paste a NullWave share string first (nullwave://p/... or NW1....).", ToastType.Warning);
            return;
        }

        // FIX: Smart Routing - Intercept track links pasted in the Profile box
        if (ShareLink.TryParseTrack(raw, out _, out _))
        {
            ToastService.Instance.Show(
                "That's a track link! Paste it into the main window's 'Add Track' box to import it.",
                type: ToastType.Info,
                durationMs: 6000,
                title: "Track Sharing");
            return;
        }

        vm.ImportShareString(raw);
    }

    private async void OnBadgeDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not UserProfileViewModel vm) return;
        var files = e.DataTransfer.TryGetFiles();
        if (files == null) return;

        foreach (var item in files)
        {
            if (item is IStorageFile file && file.Name.EndsWith(".nwbadge", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await using var stream = await file.OpenReadAsync();
                    using var reader = new StreamReader(stream);
                    var json = await reader.ReadToEndAsync();
                    vm.ImportBadge(json);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to read dropped badge file.");
                }
            }
        }
    }
}