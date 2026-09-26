using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NullWave.Models;
using Serilog;

namespace NullWave.Services;

public partial class ThemeService : ObservableObject
{
    public static ThemeService Instance { get; } = new();

    public record AccentDef(string Name, string Primary, string Secondary)
    {
        public Color PrimaryColor => Color.Parse(Primary);
        public Color SecondaryColor => Color.Parse(Secondary);
        public SolidColorBrush Brush => new SolidColorBrush(PrimaryColor);

        // Computed brushes so XAML ItemTemplates can bind directly
        public SolidColorBrush PrimaryBrush => new SolidColorBrush(PrimaryColor);
        public SolidColorBrush SecondaryBrush => new SolidColorBrush(SecondaryColor);

        // 50/50 horizontal split for duotone swatch previews.
        private LinearGradientBrush? _splitGradient;
        public LinearGradientBrush SplitGradient => _splitGradient ??= new()
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint   = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(PrimaryColor, 0),
                new GradientStop(PrimaryColor, 0.5),
                new GradientStop(SecondaryColor, 0.5),
                new GradientStop(SecondaryColor, 1)
            }
        };
    }

    public static readonly AccentDef CodenameAccent = new("Oxeye Daisy", "#EAB308", "#65A30D");

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

    [ObservableProperty] private double _sidebarWidthPx = 210;
    [ObservableProperty] private double _trackRowHeight = 44;
    [ObservableProperty] private double _rowArtSize = 40;

    //  Oxeye Daisy hero art (theme-aware) 
    public const string DarkHeroArt  = "avares://NullWave/Assets/Art/oxeye_daisy_dark.png";
    public const string LightHeroArt = "avares://NullWave/Assets/Art/oxeye_daisy_light.png";

    [ObservableProperty] private bool _isLightTheme;
    private IImage? _heroArt;
    /// <summary>Theme-matched Oxeye Daisy artwork, ready to bind to Image.Source.</summary>
    public IImage? HeroArt => _heroArt;

    public void Initialize(Preferences prefs)
    {
        // Live OS theme flips while in "System" mode must swap the art too.
        if (Application.Current != null)
            Application.Current.ActualThemeVariantChanged += (_, _) =>
            {
                UpdateThemeDependentArt();
                ApplyAccent(_lastAccentName);
            };
        ApplyAll(prefs);
    }

    private void UpdateThemeDependentArt()
    {
        RunOnUi(() =>
        {
            var light = Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light;
            IsLightTheme = light;
            try
            {
                using var stream = Avalonia.Platform.AssetLoader.Open(new Uri(light ? LightHeroArt : DarkHeroArt));
                _heroArt = new Avalonia.Media.Imaging.Bitmap(stream);
                OnPropertyChanged(nameof(HeroArt));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[ThemeService] Failed to load Oxeye Daisy hero art");
            }
        });
    }

    public void ApplyAll(Preferences p)
    {
        ApplyThemeMode(p.ThemeMode);
        ApplyAccent(p.AccentColor);
        ApplyFontScale(p.FontScale);
        ApplyDensity(p);
        ApplySidebarWidth(p.SidebarWidth);
        ApplyProfileFrame(p.ProfileFrameStyle);
    }

    private string _lastAccentName = "Oxeye Daisy";

    public void ApplyAccent(string name)
    {
        _lastAccentName = name;
        ApplyAccent(Lookup(name));
    }

    private static AccentDef Lookup(string name)
    {
        if (string.Equals(name, CodenameAccent.Name, StringComparison.OrdinalIgnoreCase)) return CodenameAccent;
        foreach (var a in BaseAccents) if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) return a;
        foreach (var a in DuotoneAccents) if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) return a;
        return CodenameAccent;
    }

    public void ApplyAccent(AccentDef def)
    {
        var primary   = Color.Parse(def.Primary);
        var secondary = Color.Parse(def.Secondary);

        // FIX: Use ActualThemeVariant to correctly resolve "System" theme preference.
        // RequestedThemeVariant returns "Default" when System is selected, breaking light mode detection.
        var theme = Application.Current?.ActualThemeVariant ?? Application.Current?.RequestedThemeVariant;
        bool light = theme == Avalonia.Styling.ThemeVariant.Light;
        
        var mixTarget = light ? Colors.White : Color.Parse("#111827");

        SetColor("ColorAccent",      primary);
        SetColor("ColorAccentHover", light ? Mix(primary, Colors.Black, 0.15) : Mix(primary, Colors.White, 0.22));
        SetColor("ColorAccentDim",   Mix(primary, mixTarget, light ? 0.85 : 0.72));
        SetColor("ColorAccentGlow",  Mix(primary, mixTarget, light ? 0.65 : 0.55));
        
        SetColor("ColorAccent2",     light ? Mix(secondary, Colors.Black, 0.35) : secondary);
        SetColor("ColorTextOnAccent", Luminance(primary) > 0.55 ? Color.Parse("#111827") : Colors.White);
        AdoptFluentSlider(primary, secondary);
        Log.Information("[ThemeService] Accent applied: {Accent}", def.Name);
    }

    private static void AdoptFluentSlider(Color primary, Color secondary)
    {
        if (Application.Current == null) return;
        var res = Application.Current.Resources;
        var light = Mix(primary, Colors.White, 0.25);

        res["SliderTrackValueFill"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = new GradientStops { new GradientStop(primary, 0), new GradientStop(secondary, 1) }
        };
        res["SliderTrackValueFillPointerOver"] = new SolidColorBrush(light);
        res["SliderTrackValueFillPressed"] = new SolidColorBrush(light);
        res["SliderThumbBackground"] = new SolidColorBrush(primary);
        res["SliderThumbBackgroundPointerOver"] = new SolidColorBrush(light);
        res["SliderThumbBackgroundPressed"] = new SolidColorBrush(light);
        res["SliderTrackFill"] = new SolidColorBrush(Application.Current.Resources.TryGetValue("ColorSliderGroove", out var b) && b is Color c ? c : Color.Parse("#2E3D5C"));
    }

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
    }

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
        SetRes("CardPadding", compact ? new Thickness(16, 14) : new Thickness(20));

        // FIX (compact mode in Settings): these three tokens drive the Settings
        // chrome (card spacing, section label spacing, page header height).
        // Previously compact mode never touched them, so Settings tabs looked
        // identical in both densities.
        SetRes("CardMargin", compact ? new Thickness(0, 0, 0, 10) : new Thickness(0, 0, 0, 16));
        SetRes("SectionLabelMargin", compact ? new Thickness(0, 16, 0, 8) : new Thickness(0, 24, 0, 12));
        SetRes("SettingsHeaderHeight", compact ? 64.0 : 84.0);

        TrackRowHeight = row;
        RowArtSize = art;
        Log.Information("[ThemeService] Density applied: compact={Compact}, row={Row}", compact, row);
    }

    public void ApplySidebarWidth(string width)
    {
        SidebarWidthPx = width switch { "Narrow" => 180, "Wide" => 300, _ => 240 };
        SetRes("SidebarWidth", SidebarWidthPx);
    }

    private Thickness _avatarFrameThickness;
    public Thickness AvatarFrameThickness
    {
        get => _avatarFrameThickness;
        set { _avatarFrameThickness = value; OnPropertyChanged(); }
    }

    private IBrush _avatarFrameBrush = Avalonia.Media.Brushes.Transparent;
    public IBrush AvatarFrameBrush
    {
        get => _avatarFrameBrush;
        set { _avatarFrameBrush = value; OnPropertyChanged(); }
    }

    public void ApplyProfileFrame(string style)
    {
        switch (style)
        {
            case "Ring":
                AvatarFrameThickness = new Thickness(3);
                AvatarFrameBrush = (IBrush)Application.Current!.Resources["BrushAccent"]!;
                break;
            case "Glow":
                AvatarFrameThickness = new Thickness(2);
                AvatarFrameBrush = (IBrush)Application.Current!.Resources["BrushAccentGlow"]!;
                break;
            default:
                AvatarFrameThickness = new Thickness(0);
                AvatarFrameBrush = Avalonia.Media.Brushes.Transparent;
                break;
        }
        Log.Information("[ThemeService] Profile frame applied: {Style}", style);
    }

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

    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    public void ApplyThemeMode(string mode)
    {
        var variant = mode switch
        {
            "Light" => Avalonia.Styling.ThemeVariant.Light,
            "Dark" => Avalonia.Styling.ThemeVariant.Dark,
            _ => Avalonia.Styling.ThemeVariant.Default
        };

        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = variant;
            UpdateThemeDependentArt();
            ApplyAccent(_lastAccentName); // re-mix accent tints for the new theme
        }
        Log.Information("[ThemeService] Theme mode applied: {Mode}", mode);
    }
}