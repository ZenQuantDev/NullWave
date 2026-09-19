using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Serilog;

namespace NullWave.Helpers;

public static class ProfileCardExporter
{
    public static async Task<string?> ExportControlToPngAsync(Control card, string suggestedName, Window owner, double scale = 2.0)
    {
        try
        {
            card.Measure(new Size(420, 1000));
            card.Arrange(new Rect(card.DesiredSize));
            var size = card.Bounds.Size;

            var rtb = new RenderTargetBitmap(
                new PixelSize((int)(size.Width * scale), (int)(size.Height * scale)),
                new Vector(96 * scale, 96 * scale));
            rtb.Render(card);

            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Profile Card",
                SuggestedFileName = suggestedName,
                FileTypeChoices = new[] { new FilePickerFileType("PNG image") { Patterns = new[] { "*.png" } } }
            });
            if (file == null) return null;

            await using var stream = await file.OpenWriteAsync();
            rtb.Save(stream);
            return file.Path.LocalPath;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ProfileCardExporter] Export failed");
            return null;
        }
    }
}