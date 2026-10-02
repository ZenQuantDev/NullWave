using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace NullWave.Views.Controls;

/// <summary>
/// OneUI/Material-You squiggle as FILLED shapes: thin groove, translucent tall
/// back-wave, slim solid front-wave (all flat-bottomed), big centred thumb.
/// Crest heights AND spacings vary (AM + FM components); flattens when paused.
/// </summary>
public class WaveSeekBar : RangeBase
{
    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<WaveSeekBar, bool>(nameof(IsPlaying));
    public static readonly StyledProperty<bool> AnimateProperty =
        AvaloniaProperty.Register<WaveSeekBar, bool>(nameof(Animate), true);
    public static readonly StyledProperty<IBrush?> WaveBrushProperty =
        AvaloniaProperty.Register<WaveSeekBar, IBrush?>(nameof(WaveBrush));
    public static readonly StyledProperty<IBrush?> GrooveBrushProperty =
        AvaloniaProperty.Register<WaveSeekBar, IBrush?>(nameof(GrooveBrush));
    public static readonly StyledProperty<IBrush?> ThumbBrushProperty =
        AvaloniaProperty.Register<WaveSeekBar, IBrush?>(nameof(ThumbBrush));

    public bool IsPlaying { get => GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }
    public bool Animate { get => GetValue(AnimateProperty); set => SetValue(AnimateProperty, value); }
    public IBrush? WaveBrush { get => GetValue(WaveBrushProperty); set => SetValue(WaveBrushProperty, value); }
    public IBrush? GrooveBrush { get => GetValue(GrooveBrushProperty); set => SetValue(GrooveBrushProperty, value); }
    public IBrush? ThumbBrush { get => GetValue(ThumbBrushProperty); set => SetValue(ThumbBrushProperty, value); }

    // --- Geometry: slim band, tall crests, clear echo ---
    private const double ThumbR       = 7.5;
    private const double BaseHeight   = 5.0;   // slim flat band
    private const double StrokeGroove = 3.0;   // thinner than the band
    private const double MaxAmp       = 7.5;   // crest rise above the band
    private const double EchoAmpMult  = 1.50;  // back-wave crests hover above front
    private const double EchoAlpha    = 0.45;  // visible on dark themes
    private const double EchoPhaseOff = 1.25;

    // --- Wave components: 3 carriers + AM envelope + FM wobble => organic ---
    // v7: shorter carriers => ~3 visible humps at half played, ~5 at full,
    // ~2 on short spans. Still broad enough to read as a ribbon, not a zigzag.
    private const double L1 = 62.0,  L2 = 38.0,  L3 = 98.0;           // carrier wavelengths
    private const double L4 = 155.0, L5 = 128.0;                      // FM / AM modulators
    private const double S1 = 0.34,  S2 = 0.60,  S3 = 0.23;           // drift speeds (cycles/s)
    private const double S4 = 0.14,  S5 = 0.09;
    private const double W1 = 0.55,  W2 = 0.25,  W3 = 0.20;

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastTicks;
    private double _p1, _p2, _p3, _p4, _p5;
    private double _amp;
    private bool _dragging;

    public WaveSeekBar()
    {
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, OnTick);
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPlayingProperty || change.Property == AnimateProperty || change.Property == IsVisibleProperty)
            UpdateTimer();

        if (change.Property == ValueProperty || change.Property == MinimumProperty ||
            change.Property == MaximumProperty || change.Property == WaveBrushProperty ||
            change.Property == GrooveBrushProperty || change.Property == ThumbBrushProperty)
            InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    private void UpdateTimer()
    {
        if (!Animate || !IsEffectivelyVisible)
        {
            _timer.Stop();
            if (!Animate) { _amp = 0; InvalidateVisual(); }
            return;
        }
        if ((IsPlaying || _amp > 0.02) && !_timer.IsEnabled)
        {
            _lastTicks = _clock.ElapsedTicks;
            _timer.Start();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = _clock.ElapsedTicks;
        var dt = Math.Min(0.1, (now - _lastTicks) / (double)Stopwatch.Frequency);
        _lastTicks = now;

        if (IsPlaying)
        {
            _p1 = (_p1 + dt * Math.PI * 2 * S1) % (Math.PI * 2);
            _p2 = (_p2 + dt * Math.PI * 2 * S2) % (Math.PI * 2);
            _p3 = (_p3 + dt * Math.PI * 2 * S3) % (Math.PI * 2);
            _p4 = (_p4 + dt * Math.PI * 2 * S4) % (Math.PI * 2);
            _p5 = (_p5 + dt * Math.PI * 2 * S5) % (Math.PI * 2);
        }

        var target = IsPlaying ? MaxAmp : 0.0;
        _amp += (target - _amp) * Math.Min(1, dt * (IsPlaying ? 6 : 8)); // ~0.35s flatten on pause

        if (!IsPlaying && _amp < 0.02) { _amp = 0; _timer.Stop(); } // flat + idle: stop repaints
        if (IsEffectivelyVisible) InvalidateVisual();
    }

    /// <summary>Crest height (0..amp) at x: 3 carriers, FM-wobbled spacing, AM-varied heights.</summary>
    private double WaveHeight(double x, double amp, double phOff, double wavelengthScale)
    {
        double fm = 0.45 * Math.Sin(2 * Math.PI * x / (L4 * wavelengthScale) + _p4 + phOff * 0.5);
        double n = W1 * Math.Sin(2 * Math.PI * x / (L1 * wavelengthScale) + _p1 + phOff + fm)
                 + W2 * Math.Sin(2 * Math.PI * x / (L2 * wavelengthScale) + _p2 + phOff * 1.7 + fm * 0.6)
                 + W3 * Math.Sin(2 * Math.PI * x / (L3 * wavelengthScale) + _p3 + phOff * 0.6);
        // AM floor raised to 0.40 so "suppressed" crests stay visible as short humps
        double am = 0.70 + 0.30 * Math.Sin(2 * Math.PI * x / (L5 * wavelengthScale) + _p5 + phOff * 1.3);
        double s = 0.5 + 0.5 * n;
        return amp * am * Math.Pow(s, 1.1);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        ctx.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // hit-test surface

        var range = Maximum - Minimum;
        var frac = range > 0 ? Math.Clamp((Value - Minimum) / range, 0, 1) : 0;
        double x0 = ThumbR, x1 = w - ThumbR;
        double midY = h / 2;
        double yBottom = midY + BaseHeight / 2.0;   // ALWAYS-FLAT bottom edge
        double px = x0 + (x1 - x0) * frac;

        var wave   = WaveBrush   ?? Brushes.White;
        var groove = GrooveBrush ?? Brushes.Gray;
        var thumb  = ThumbBrush  ?? wave;

        // Layer 1: unplayed groove
        if (px < x1)
            ctx.DrawLine(new Pen(groove, StrokeGroove, lineCap: PenLineCap.Round),
                         new Point(px, midY), new Point(x1, midY));

        // Layers 2+3: filled waves, tapered to the flat band at both ends.
        // Wavelength scales with played width (clamped) so short spans still show humps.
        if (px - x0 > 1)
        {
            double played = px - x0;
            double wScale = Math.Clamp(played / 240.0, 0.50, 1.0);
            FillWave(ctx, wave, EchoAlpha, x0, px, yBottom, _amp * EchoAmpMult, EchoPhaseOff, wScale);
            FillWave(ctx, wave, 1.0,       x0, px, yBottom, _amp,              0.0,         wScale);
            double capR = BaseHeight / 2.0;
            ctx.DrawEllipse(wave, null, new Rect(x0 - capR, midY - capR, capR * 2, capR * 2));
        }
        else if (px - x0 > 0)
        {
            double capR = BaseHeight / 2.0;
            ctx.DrawEllipse(wave, null, new Rect(x0 - capR, midY - capR, capR * 2, capR * 2));
        }

        // Thumb knob on the baseline
        var r = _dragging ? ThumbR + 2 : ThumbR;
        ctx.DrawEllipse(thumb, null, new Rect(px - r, midY - r, r * 2, r * 2));
    }

    /// <summary>Fills the region between the flat bottom edge and a wavy top edge.</summary>
    private void FillWave(DrawingContext ctx, IBrush brush, double opacity,
                          double xa, double xb, double yBottom, double amp, double phOff, double wScale)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            double width = xb - xa;
            int steps = Math.Max((int)(width / 2.5), 16); // finer sampling for shorter waves

            g.BeginFigure(new Point(xa, yBottom - BaseHeight), true);
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                double x = xa + width * t;
                double taper = Math.Sin(Math.PI * t);          // 0 at both ends -> flat joins
                double y = yBottom - BaseHeight - WaveHeight(x, amp, phOff, wScale) * taper;
                g.LineTo(new Point(x, y));
            }
            g.LineTo(new Point(xb, yBottom));                  // right edge down to flat bottom
            g.LineTo(new Point(xa, yBottom));                  // flat bottom edge back to start
            g.EndFigure(true);
        }

        if (opacity >= 1.0)
        {
            ctx.DrawGeometry(brush, null, geo);
        }
        else
        {
            using (ctx.PushOpacity(opacity))
                ctx.DrawGeometry(brush, null, geo);
        }
    }

    private void SetFromX(double x)
    {
        var track = Math.Max(1, Bounds.Width - ThumbR * 2);
        Value = Minimum + Math.Clamp((x - ThumbR) / track, 0, 1) * (Maximum - Minimum);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragging = true;
        e.Pointer.Capture(this);
        SetFromX(e.GetPosition(this).X); // not marked handled: MiniPlayerView.OnSeekPressed still fires
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging) SetFromX(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging) e.Pointer.Capture(null);
        _dragging = false;
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
        InvalidateVisual();
    }
}