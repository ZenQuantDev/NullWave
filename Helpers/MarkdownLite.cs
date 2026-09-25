using System;
using System.Collections.Generic;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace NullWave.Helpers;

/// <summary>
/// Minimal markdown renderer for the What's New dialog.
/// Supports #/##/### headings, "- " bullets, **bold**, `inline code`, plain paragraphs.
/// Intentionally not a full markdown engine: small, dependency-free, and styled via the
/// md-* classes defined in WhatsNewWindow.axaml so it follows the unified theme tokens.
/// </summary>
public static class MarkdownLite
{
    public static List<Control> Render(string markdown)
    {
        var controls = new List<Control>();
        if (string.IsNullOrWhiteSpace(markdown)) return controls;

        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').TrimEnd();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("---")) continue; // horizontal rule: skip

            if (line.StartsWith("### "))     controls.Add(Block(line[4..], "md-h2"));
            else if (line.StartsWith("## ")) controls.Add(Block(line[3..], "md-h1"));
            else if (line.StartsWith("# "))  controls.Add(Block(line[2..], "md-h1"));
            else if (line.StartsWith("- "))  controls.Add(Block(line[2..], "md-bullet", "•  "));
            else if (line.StartsWith("* "))  controls.Add(Block(line[2..], "md-bullet", "•  "));
            else                             controls.Add(Block(line, "md-p"));
        }
        return controls;
    }

    private static TextBlock Block(string text, string styleClass, string? prefix = null)
    {
        var tb = new TextBlock();
        tb.Classes.Add(styleClass);
        if (prefix != null) tb.Inlines?.Add(new Run(prefix));
        AppendInlineRuns(tb, text);
        return tb;
    }

    /// <summary>Parses **bold** and `code` spans into styled Runs; everything else stays plain.</summary>
    private static void AppendInlineRuns(TextBlock target, string text)
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
                        FontFamily = new FontFamily("Consolas, monospace")
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