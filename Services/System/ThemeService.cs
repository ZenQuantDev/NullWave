using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NullWave.Models;
using Serilog;

namespace NullWave.Services;

/// <summary>
/// Live theme engine for v0.5 "Blue Orchid". Single source of truth that
/// translates Preferences into Avalonia resources at runtime: accent (base +
/// signature + 10 duotones), font scale, global density, sidebar width.
/// Dark-only for v0.5; glass + light theme arrive in v0.6.
/// NOTE: BrushAccentGradient lives in Colors.axaml with DynamicResource stops,
/// so it follows ColorAccent/ColorAccent2 automatically — never rebuild it here.
/// </summary>
public partial class ThemeService : ObservableObject
{
    public static ThemeService Instance { get; } = new();

    public record AccentDef(string Name, string Primary, string Secondary);

    /// <summary>v0.5 signature — vivid azure + orchid duo.</summary>
    public static readonly AccentDef CodenameAccent =
        new("Blue Orchid", "#4E9BFF", "#B08CFF");

    public static readonly IReadOnlyList<AccentDef> BaseAccents = new[]
    {
        new AccentDef("Purple", "#8B5CF6", "#C4B5FD"),
        new AccentDef("Sky",    "#38BDF8", "#7DD3FC"),
        new AccentDef("Green",  "#34D399", "#6EE7B7"),
        new AccentDef("Amber",  "#FCD34D", "#FDE68A"),
        new AccentDef("Red",    "#F87171", "#FCA5A5"),
        new AccentDef("Pink",   "#F472B6", "#F9A8D4"),
        new AccentDef("Orange", "#FB923C", "#FDBA74"),
        new AccentDef("Teal",   "#2DD4BF", "#5EEAD4"),
        new AccentDef("Lime",   "#A3E635", "#BEF264"),
    };

    /// <summary>10 complementary (color-wheel opposite) duotone pairs.</summary>
    public static readonly IReadOnlyList<AccentDef> DuotoneAccents = new[]
    {
        new AccentDef("Violet & Lime",    "#8B5CF6", "#A3E635"),
        new AccentDef("Navy & Gold",      "#2563EB", "#FCD34D"),
        new AccentDef("Crimson & Ice",    "#EF4444", "#7DD3FC"),
        new AccentDef("Orchid & Mint",    "#A78BFA", "#34D399"),
        new AccentDef("Teal & Coral",     "#14B8A6", "#FB7185"),
        new AccentDef("Magenta & Spring", "#D946EF", "#84CC16"),
        new AccentDef("Amber & Indigo",   "#F59E0B", "#6366F1"),
        new AccentDef("Cyan & Sunset",    "#06B6D4", "#FB923C"),
        new AccentDef("Rose & Jade",      "#F43F5E", "#10B981"),
        new AccentDef("Azure & Peach",    "#3B82F6", "#FDBA74"),
    };

    // Bindable layout state (views bind via x:Static ThemeService.Instance)
    [ObservableProperty] private double _sidebarWidthPx = 210;
    [ObservableProperty] private double _trackRowHeight = 44;
    [ObservableProperty] private double _rowArtSize = 40;

    public void Initialize(Preferences prefs) => ApplyAll(prefs);

    public void ApplyAll(Preferences p)
    {
        ApplyAccent(p.AccentColor);
        ApplyFontScale(p.FontScale);
        ApplyDensity(p);
        ApplySidebarWidth(p.SidebarWidth);
    }

    //  Accent 
    public void ApplyAccent(string name) => ApplyAccent(Lookup(name));

    private static AccentDef Lookup(string name)
    {
        if (string.Equals(name, CodenameAccent.Name, StringComparison.OrdinalIgnoreCase))
            return CodenameAccent;
        foreach (var a in BaseAccents)
            if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) return a;
        foreach (var a in DuotoneAccents)
            if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) return a;
        return CodenameAccent;
    }

    public void ApplyAccent(AccentDef def)
    {
        var primary = Color.Parse(def.Primary);
        var secondary = Color.Parse(def.Secondary);
        var surfaceBase = Color.Parse("#111827");

        SetColor("ColorAccent", primary);
        SetColor("ColorAccentHover", Mix(primary, Colors.White, 0.22));
        SetColor("ColorAccentDim", Mix(primary, surfaceBase, 0.72));
        SetColor("ColorAccentGlow", Mix(primary, surfaceBase, 0.55));
        SetColor("ColorAccent2", secondary);
        SetColor("ColorTextOnAccent", Luminance(primary) > 0.55 ? Color.Parse("#111827") : Colors.White);

        AdoptFluentSlider(primary, secondary);
        Log.Information("[ThemeService] Accent applied: {Accent}", def.Name);
    }

    /// <summary>
    /// Fluent Slider template reads these keys via DynamicResource, so replacing
    /// the objects here is safe and makes BOTH sliders show the duo gradient.
    /// </summary>
    private static void AdoptFluentSlider(Color primary, Color secondary)
    {
        if (Application.Current == null) return;
        var res = Application.Current.Resources;
        var light = Mix(primary, Colors.White, 0.25);

        res["SliderTrackValueFill"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(primary, 0),
                new GradientStop(secondary, 1)
            }
        };
        res["SliderTrackValueFillPointerOver"] = new SolidColorBrush(light);
        res["SliderTrackValueFillPressed"] = new SolidColorBrush(light);
        res["SliderThumbBackground"] = new SolidColorBrush(primary);
        res["SliderThumbBackgroundPointerOver"] = new SolidColorBrush(light);
        res["SliderThumbBackgroundPressed"] = new SolidColorBrush(light);
        res["SliderTrackFill"] = new SolidColorBrush(Color.Parse("#2E3D5C"));
    }

    //  Font scale 
    private static readonly (string Key, double Base)[] FontScaleTable =
    {
        ("FontSizeXs", 11), ("FontSizeSm", 12), ("FontSizeBase", 14), ("FontSizeMd", 15),
        ("FontSizeLg", 17), ("FontSizeXl", 20), ("FontSize2xl", 24), ("FontSize3xl", 30),
        ("FontSizeLabel", 11), ("FontSizeBody", 14), ("FontSizeBodyLarge", 15),
        ("FontSizeSubtitle", 17), ("FontSizeTitle", 20), ("FontSizeHeading", 24)
    };

    public void ApplyFontScale(string scale)
    {
        double mult = scale switch { "Small" => 0.92, "Large" => 1.10, _ => 1.0 };
        foreach (var (key, basis) in FontScaleTable)
            SetRes(key, Math.Round(basis * mult, 1));
        Log.Information("[ThemeService] Font scale applied: {Scale} (x{Mult})", scale, mult);
    }

    //  Density (row style + global compact override) 
    public void ApplyDensity(Preferences p)
    {
        bool compact = p.CompactMode;
        (double row, double art, double title, double sub,
         double mini, double miniArt, Thickness rowMargin, Thickness navPad) =
            compact
                ? (30.0, 22.0, 12.0, 11.0, 78.0, 44.0, new Thickness(8, 1), new Thickness(10, 3))
                : p.TrackRowStyle switch
                {
                    "Compact" => (32.0, 24.0, 13.0, 11.0, 90.0, 52.0, new Thickness(8, 2), new Thickness(12, 8)),
                    "Cozy"    => (56.0, 48.0, 16.0, 12.0, 96.0, 52.0, new Thickness(8, 8), new Thickness(12, 10)),
                    _         => (44.0, 40.0, 15.0, 12.0, 90.0, 52.0, new Thickness(8, 5), new Thickness(12, 8)),
                };

        SetRes("TrackRowHeight", row);
        SetRes("RowArtSize", art);
        SetRes("RowTitleSize", title);
        SetRes("RowSubSize", sub);
        SetRes("MiniPlayerHeight", mini);
        SetRes("MiniArtSize", miniArt);
        SetRes("RowMargin", rowMargin);
        SetRes("NavPadding", navPad);
        
        SetRes("DetailArtHeight", compact ? 140.0 : 220.0);
        SetRes("QueueRowHeight", compact ? 36.0 : 48.0);
        SetRes("QueueArtSize", compact ? 28.0 : 40.0);

        TrackRowHeight = row;
        RowArtSize = art;
        Log.Information("[ThemeService] Density applied: compact={Compact}, style={Style}",
            p.CompactMode, p.TrackRowStyle);
    }

    //  Sidebar width 
    public void ApplySidebarWidth(string width)
    {
        SidebarWidthPx = width switch { "Narrow" => 180, "Wide" => 300, _ => 240 };
        SetRes("SidebarWidth", SidebarWidthPx);
        Log.Information("[ThemeService] Sidebar width applied: {Width}", width);
    }

    //  Plumbing 
    private static void SetColor(string key, Color color) =>
        RunOnUi(() => { if (Application.Current != null) Application.Current.Resources[key] = color; });

    private static void SetRes(string key, object value) =>
        RunOnUi(() => { if (Application.Current != null) Application.Current.Resources[key] = value; });

    private static void RunOnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    private static Color Mix(Color a, Color b, double t) => new Color(255,
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

    private static double Luminance(Color c) =>
        (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
}