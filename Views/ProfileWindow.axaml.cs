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

    /// <summary>
    /// Pre-generates the share code + QR so the card is export-ready the moment
    /// the window opens, and sniffs the clipboard for a friend's share string.
    /// </summary>
    private async void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnWindowLoaded;
        if (DataContext is not UserProfileViewModel vm) return;

        vm.EnsureShareAssets();

        // Clipboard auto-detection: offer import if a share string is already copied.
        try
        {
            // Reflection bridge bypasses Avalonia 12 compile-time clipboard API differences
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            var text = await ClipboardTextReader.TryGetTextAsync(clipboard);

            if (string.IsNullOrWhiteSpace(text)) return;
            if (!ShareLink.TryParseProfile(text, out var encoded)) return;

            var payload = ProfileShareService.Decode(encoded);
            if (payload == null) return;

            // Never auto-import the user's own card.
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

        // Refresh code/QR so the exported card always carries current stats.
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
            // Make sure the QR tile is painted before we snapshot the card.
            if (vm.ShareQrBitmap != null) QrImage.Source = vm.ShareQrBitmap;

            // Remember the live size so we can restore the on-screen layout afterwards.
            var liveSize = ProfileCard.Bounds.Size;

            // FIX: measure with UNBOUNDED height so the card grows to fit
            // banner + identity + QR footer. The old constrained measure produced a
            // short card where the footer collapsed on top of the stat chips.
            ProfileCard.Measure(new Size(liveSize.Width, double.PositiveInfinity));
            ProfileCard.Arrange(new Rect(new Point(0, 0), ProfileCard.DesiredSize));
            ProfileCard.UpdateLayout();

            // 2x scale: crisp for social sharing, ~4x fewer pixels than the old 3x pass.
            const double scale = 2.0;
            var pixelSize = new PixelSize(
                Math.Max(1, (int)(ProfileCard.DesiredSize.Width * scale)),
                Math.Max(1, (int)(ProfileCard.DesiredSize.Height * scale)));

            using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96 * scale, 96 * scale));
            bitmap.Render(ProfileCard);

            // Restore the live layout immediately so the on-screen card isn't left stretched.
            ProfileCard.Measure(liveSize);
            ProfileCard.Arrange(new Rect(liveSize));
            ProfileCard.InvalidateVisual();

            var ext = Path.GetExtension(file.Path.LocalPath).ToLowerInvariant();
            await using var outStream = await file.OpenWriteAsync();

            if (ext is ".jpg" or ".jpeg")
            {
                // The photographic banner is high-entropy content that PNG compresses
                // poorly (that's the 1.5 MB). Re-encode lossy via SkiaSharp at q92:
                // visually identical for sharing, ~10x smaller. QR stays scannable
                // thanks to 2x resolution + QR error correction.
                using var pngMs = new MemoryStream();
                bitmap.Save(pngMs);
                pngMs.Position = 0;
                using var sk = SkiaSharp.SKBitmap.Decode(pngMs);
                using var jpg = sk.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 92);
                jpg.SaveTo(outStream);
            }
            else
            {
                // Lossless path for users who want a pristine PNG.
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