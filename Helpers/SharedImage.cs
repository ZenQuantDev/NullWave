using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace NullWave.Helpers;

/// <summary>
/// Wraps a cached Bitmap to prevent Avalonia's Image control from disposing it
/// when the control is recycled or unloaded. Avalonia disposes IImage sources that
/// implement IDisposable; this wrapper intentionally does NOT implement IDisposable
/// so the shared underlying Bitmap survives container recycling and virtualization.
/// All members that touch the inner Bitmap swallow ObjectDisposedException: a rare
/// dispose race must degrade to an invisible image, never a fatal layout crash.
/// </summary>
public sealed class SharedImage : IImage
{
    private readonly Bitmap _bitmap;

    public SharedImage(Bitmap bitmap) => _bitmap = bitmap;

    public Size Size
    {
        get
        {
            try
            {
                // Cast to IImage: in Avalonia 12 Bitmap's members may be explicitly
                // implemented; the interface call is the stable path.
                return ((IImage)_bitmap).Size;
            }
            catch (ObjectDisposedException)
            {
                return default; // measure as empty instead of crashing the layout pass
            }
        }
    }

    public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
    {
        try
        {
            ((IImage)_bitmap).Draw(context, sourceRect, destRect);
        }
        catch (ObjectDisposedException)
        {
            /* Swallow: underlying bitmap was disposed elsewhere, nothing to draw */
        }
    }
}