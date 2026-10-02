using System;
using System.Collections.Generic;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Material.Icons;
using Material.Icons.Avalonia;
using NullWave.Helpers;

namespace NullWave.Views.Controls;

/// <summary>
/// Renders the latest CHANGELOG.md release: version header line, then colored
/// section pills (Added/Changed/Fixed/...) with wrapped bullet rows.
/// Shared by AboutTab and WhatsNewWindow so both read from one source of truth.
/// The version header is rendered here (from the markdown itself), not injected
/// by host windows/tabs.
/// </summary>
public partial class ChangelogView : UserControl
{
    private readonly struct SectionStyle
    {
        public SectionStyle(MaterialIconKind icon, IBrush foreground, IBrush background)
        { Icon = icon; Foreground = foreground; Background = background; }
        public MaterialIconKind Icon { get; }
        public IBrush Foreground { get; }
        public IBrush Background { get; }
    }

    public ChangelogView()
    {
        InitializeComponent();
        // Defer until attached to the visual tree so Application-level
        // resources (theme brushes) resolve correctly.
        Loaded += (_, _) => Render();
    }

    private IBrush Res(string key, IBrush fallback) =>
        this.TryFindResource(key, out var v) && v is IBrush b ? b : fallback;

    private void Render()
    {
        RootPanel.Children.Clear();

        var textBrush = Res("BrushTextPrimary", Brushes.Black);
        var mutedBrush = Res("BrushTextMuted", Brushes.Gray);
        var codeFg = Res("BrushInlineCode", Res("BrushAccentHover", Brushes.DarkOrange));
        var neutralBg = Res("BrushSurface2", Res("BrushElevated", Brushes.LightGray));

        var styles = new Dictionary<string, SectionStyle>(StringComparer.OrdinalIgnoreCase)
        {
            ["Added"]    = new(MaterialIconKind.PlusCircleOutline,   Res("BrushStatusSuccess", Brushes.Green),  Res("BrushGreenDim", neutralBg)),
            ["Changed"]  = new(MaterialIconKind.SwapHorizontal,      Res("BrushStatusWarning", Brushes.Orange), Res("BrushAmberDim", neutralBg)),
            ["Fixed"]    = new(MaterialIconKind.Wrench,              Res("BrushStatusInfo", Brushes.Blue),      Res("BrushBlueDim", neutralBg)),
            ["Removed"]  = new(MaterialIconKind.MinusCircleOutline,  mutedBrush,                                neutralBg),
            ["Security"] = new(MaterialIconKind.ShieldAccount,       Res("BrushStatusError", Brushes.Red),      Res("BrushRedDim", neutralBg)),
        };
        var defaultStyle = new SectionStyle(MaterialIconKind.InformationOutline, mutedBrush, neutralBg);

        var sections = ChangelogParser.GetLatestReleaseSections();
        if (sections.Count == 0)
        {
            RootPanel.Children.Add(new TextBlock
            {
                Text = "No release notes available.",
                Foreground = mutedBrush,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        // FIX: version header ("[0.6.2] - 25-Sep-2026 ...") now renders from the
        // markdown itself, inside the changelog context, for every host.
        var versionHeader = ChangelogParser.GetLatestVersionHeader();
        if (!string.IsNullOrWhiteSpace(versionHeader))
        {
            RootPanel.Children.Add(new TextBlock
            {
                Text = versionHeader,
                Foreground = mutedBrush,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });
        }

        bool first = true;
        foreach (var section in sections)
        {
            var style = styles.TryGetValue(section.Header, out var s) ? s : defaultStyle;

            var pill = new Border
            {
                Background = style.Background,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, first ? 0 : 16, 0, 10),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new MaterialIcon
                        {
                            Kind = style.Icon,
                            Width = 14,
                            Height = 14,
                            Foreground = style.Foreground,
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = section.Header,
                            Foreground = style.Foreground,
                            FontWeight = FontWeight.Bold,
                            FontSize = 12,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                }
            };
            RootPanel.Children.Add(pill);
            first = false;

            foreach (var bullet in section.Bullets)
            {
                var text = new TextBlock
                {
                    Foreground = textBrush,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13,
                    LineHeight = 20,
                    Margin = new Thickness(4, 0, 0, 8)
                };
                text.Inlines?.Add(new Run("•  ") { Foreground = mutedBrush });
                AppendInlineRuns(text, bullet, codeFg);
                RootPanel.Children.Add(text);
            }
        }
    }

    /// <summary>Parses **bold** and `code` spans into styled runs; everything else stays plain.</summary>
    private static void AppendInlineRuns(TextBlock target, string text, IBrush codeFg)
    {
        if (string.IsNullOrEmpty(text)) return;

        var plain = new StringBuilder();
        int i = 0;

        void Flush()
        {
            if (plain.Length <= 0) return;
            target.Inlines?.Add(new Run(plain.ToString()));
            plain.Clear();
        }

        while (i < text.Length)
        {
            if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end > i)
                {
                    Flush();
                    target.Inlines?.Add(new Run(text[(i + 2)..end]) { FontWeight = FontWeight.SemiBold });
                    i = end + 2;
                    continue;
                }
            }

            if (text[i] == '`')
            {
                int end = text.IndexOf('`', i + 1);
                if (end > i)
                {
                    Flush();
                    target.Inlines?.Add(new Run(text[(i + 1)..end])
                    {
                        FontFamily = new FontFamily("Consolas, Menlo, monospace"),
                        FontWeight = FontWeight.SemiBold,
                        Foreground = codeFg
                    });
                    i = end + 1;
                    continue;
                }
            }

            plain.Append(text[i]);
            i++;
        }
        Flush();
    }
}