using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NullWave.Helpers;
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
        public SolidColorBrush PrimaryBrush => new SolidColorBrush(PrimaryColor);
        public SolidColorBrush SecondaryBrush => new SolidColorBrush(SecondaryColor);

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

    public record AppearancePresetDef(string Id, string NameKey, string DescriptionKey, string ThemeMode, string AccentColor, string TrackRowStyle, string FontScale, string? SceneId = null, int? WallpaperOpacity = null);

    public static readonly IReadOnlyList<AppearancePresetDef> AppearancePresets = new[]
    {
        new AppearancePresetDef("OxeyeClassic", "Settings_Appearance_Preset_OxeyeClassic", "Settings_Appearance_Preset_OxeyeClassic_Desc", "Dark", "Oxeye Daisy", "Comfortable", "Medium", "spotlight", 40),
        new AppearancePresetDef("MidnightOled", "Settings_Appearance_Preset_MidnightOled", "Settings_Appearance_Preset_MidnightOled_Desc", "TrueBlack", "Purple", "Compact", "Medium", "dusk", 50),
        new AppearancePresetDef("StudioLight", "Settings_Appearance_Preset_StudioLight", "Settings_Appearance_Preset_StudioLight_Desc", "Light", "Sky", "Comfortable", "Medium", "horizon", 35),
        new AppearancePresetDef("FocusMinimal", "Settings_Appearance_Preset_FocusMinimal", "Settings_Appearance_Preset_FocusMinimal_Desc", "Dark", "Teal", "Compact", "Small", "none", null),
    };

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

    public const string DarkHeroArt  = "avares://NullWave/Assets/Art/oxeye_daisy_dark.png";
    public const string LightHeroArt = "avares://NullWave/Assets/Art/oxeye_daisy_light.png";

    [ObservableProperty] private bool _isLightTheme;
    private IImage? _heroArt;
    public IImage? HeroArt => _heroArt;

    //  True Black OLED palette 
    // Applied as ROOT-LEVEL color overrides, not a custom ThemeVariant: Avalonia
    // resolves ThemeDictionaries against built-in variant keys, so an
    // x:Key="TrueBlack" dictionary silently falls back to Dark. Root overrides
    // use the same proven mechanism as accents. TrueBlack rides on the Dark
    // variant for Fluent control styling; only the palette differs.
    private static readonly IReadOnlyDictionary<string, string> TrueBlackPalette = new Dictionary<string, string>
    {
        ["ColorBase"]          = "#000000",
        ["ColorSurface"]       = "#000000",
        ["ColorSurfaceTranslucent"] = "#8C000000",
        ["ColorSurfaceHeavyTranslucent"] = "#73000000",
        ["ColorRowBackdrop"] = "#CC000000",
        ["ColorBaseTranslucent"] = "#E6000000",
        ["ColorWindowScrim"] = "#BF000000",
        ["ColorSurface2"]      = "#0A0A0A",
        ["ColorElevated"]      = "#0A0A0A",
        ["ColorHover"]         = "#141414",
        ["ColorBorder"]        = "#1A1A1A",
        ["ColorBorderSub"]     = "#242424",
        ["ColorSliderGroove"]  = "#1A1A1A",
        ["ColorPlayerIcon"]    = "#A8B4CC",
        ["ColorTextPrimary"]   = "#F0F0F0",
        ["ColorTextSecondary"] = "#9BA3AF",
        ["ColorTextMuted"]     = "#7A8599",  
        ["ColorStarOn"]        = "#F59E0B",
        ["ColorStarOff"]       = "#4B5563",
        ["ColorAmberDark"]     = "#D97706",
        ["ColorInlineCode"]    = "#FCD34D",
        ["ColorAccentDim"]     = "#0F1A2E",
        ["ColorAccentGlow"]    = "#16233D",
        ["ColorBlueDim"]       = "#0F1A2E",
        ["ColorAmberDim"]      = "#2A1A00",
        ["ColorGreenDim"]      = "#02231A",
        ["ColorRedDim"]        = "#2A0505",
        ["ColorShuffle"]       = "#8B7CF6",
        ["ColorShuffleSmart"]  = "#D97706",
    };

    private string _currentMode = "Dark";

    // FIX (accents only applied on theme flip): separate the REQUESTED accent
    // (_lastAccentName, re-applied on theme/variant changes) from the accent
    // actually WRITTEN to resources (_appliedAccentName + _appliedAccentMode).
    // The old guard compared the requested name, which the string overload sets
    // before delegating - so it was always "equal" and swallowed every click.
    private string _lastAccentName = "Oxeye Daisy";
    private string _appliedAccentName = string.Empty;
    private string _appliedAccentMode = string.Empty;
    private bool _paletteWasRewritten;

    // FIX (profile frame stale brush): remember the requested frame style so every
    // accent/theme remix can re-resolve its brush from the live resources.
    private string _currentProfileFrame = "None";

    public void Initialize(Preferences prefs)
    {
        if (Application.Current != null)
            Application.Current.ActualThemeVariantChanged += (_, _) =>
            {
                // DEDUPE: this event also fires when *we* assign RequestedThemeVariant
                // inside ApplyThemeMode - and ApplyThemeMode already re-runs
                // UpdateThemeDependentArt() + ApplyAccent() explicitly at the end.
                // Reacting here doubled the hero-art decode and accent remix on every
                // Dark<->Light click (TrueBlack maps to the Dark variant, so only
                // clicks crossing the Light/Dark boundary triggered the double work).
                // Any variant change while an explicit mode is active is self-inflicted;
                // only OS-driven flips under System mode are genuinely external.
                if (!string.Equals(_currentMode, "System", StringComparison.OrdinalIgnoreCase)) return;
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

    public void ApplyAccent(string name)
    {
        _lastAccentName = name;
        ApplyAccentCore(Lookup(name));
    }

    public void ApplyAccent(AccentDef def) => ApplyAccentCore(def);

    private static AccentDef Lookup(string name)
    {
        if (string.Equals(name, CodenameAccent.Name, StringComparison.OrdinalIgnoreCase)) return CodenameAccent;
        foreach (var a in BaseAccents) if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) return a;
        foreach (var a in DuotoneAccents) if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) return a;
        return CodenameAccent;
    }

    private string AccentModeKey(bool light) =>
        light ? "Light" : _currentMode == "TrueBlack" ? "TrueBlack" : "Dark";

    private void ApplyAccentCore(AccentDef def)
    {
        var primary   = Color.Parse(def.Primary);
        var secondary = Color.Parse(def.Secondary);

        var theme = Application.Current?.ActualThemeVariant ?? Application.Current?.RequestedThemeVariant;
        bool light = theme == Avalonia.Styling.ThemeVariant.Light;
        var modeKey = AccentModeKey(light);

        // Skip only TRUE no-ops: same accent already written for the same mode.
        if (!ThemeGuard.ShouldRemixAccent(_appliedAccentName, _appliedAccentMode, def.Name, modeKey, _paletteWasRewritten)) return;
        _appliedAccentName = def.Name;
        _appliedAccentMode = modeKey;
        _paletteWasRewritten = false;

        var kind = light ? ThemeKind.Light : _currentMode == "TrueBlack" ? ThemeKind.TrueBlack : ThemeKind.Dark;
        var palette = AccentPalette.Compute(primary, secondary, kind);

        SetColor("ColorAccent", palette.Accent);
        SetColor("ColorAccentHover", palette.AccentHover);
        SetColor("ColorAccentDim", palette.AccentDim);
        SetColor("ColorAccentGlow", palette.AccentGlow);
        SetColor("ColorAccent2", palette.Accent2);
        SetColor("ColorAccent2Dim", palette.Accent2Dim);
        SetColor("ColorAccent2Glow", palette.Accent2Glow);
        SetColor("ColorTextOnAccent", palette.TextOnAccent);
        AdoptFluentSlider(primary, secondary);

        // FIX (profile frame stale brush): Ring/Glow capture a brush reference; re-resolve
        // it after every remix so frames never keep a pre-flip brush instance.
        ApplyProfileFrame(_currentProfileFrame);

        Log.Debug("[ThemeService] Accent applied: {Accent} ({Mode})", def.Name, modeKey);
    }

    private static void AdoptFluentSlider(Color primary, Color secondary)
    {
        if (Application.Current == null) return;
        var res = Application.Current.Resources;
        var light = AccentPalette.Mix(primary, Colors.White, 0.25);

        res["SliderTrackValueFill"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint   = new RelativePoint(1, 0.5, RelativeUnit.Relative),
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
        SetRes("CardMargin", compact ? new Thickness(0, 0, 0, 10) : new Thickness(0, 0, 0, 16));
        SetRes("SectionLabelMargin", compact ? new Thickness(0, 16, 0, 8) : new Thickness(0, 24, 0, 12));
        SetRes("SettingsHeaderHeight", compact ? 64.0 : 84.0);

        TrackRowHeight = row;
        RowArtSize = art;
        Log.Debug("[ThemeService] Density applied: compact={Compact}, row={Row}", compact, row);
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
        _currentProfileFrame = style;
        RunOnUi(() =>
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
            Log.Debug("[ThemeService] Profile frame applied: {Style}", style);
        });
    }

    private static void SetColor(string key, Color color) =>
        RunOnUi(() => { if (Application.Current != null) Application.Current.Resources[key] = color; });

    private static void RemoveColor(string key) =>
        RunOnUi(() => { if (Application.Current != null) Application.Current.Resources.Remove(key); });

    private static void SetRes(string key, object value) =>
        RunOnUi(() => { if (Application.Current != null) Application.Current.Resources[key] = value; });

    private static void RunOnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    public void ApplyThemeMode(string mode)
    {
        _currentMode = mode;

        var variant = mode switch
        {
            "Light"     => Avalonia.Styling.ThemeVariant.Light,
            "Dark"      => Avalonia.Styling.ThemeVariant.Dark,
            "TrueBlack" => Avalonia.Styling.ThemeVariant.Dark,
            _           => Avalonia.Styling.ThemeVariant.Default
        };

        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = variant;

            // Clear previous True Black overrides FIRST so Dark/Light/System
            // restore their XAML palette values exactly.
            foreach (var key in TrueBlackPalette.Keys)
                RemoveColor(key);

            if (mode == "TrueBlack")
                foreach (var kv in TrueBlackPalette)
                    SetColor(kv.Key, Color.Parse(kv.Value));

            _paletteWasRewritten = true;
            UpdateThemeDependentArt();
            ApplyAccent(_lastAccentName); // re-mix accent tints for the new mode (also re-resolves profile frame)
        }
        Log.Information("[ThemeService] Theme mode applied: {Mode}", mode);
    }
}