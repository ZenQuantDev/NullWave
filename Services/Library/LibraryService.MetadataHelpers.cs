using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NullWave.Helpers;
using NullWave.Models;

namespace NullWave.Services;

public partial class LibraryService
{
    private bool HasVerifiedFile(Track t)
    {
        if (string.IsNullOrEmpty(t.FilePath) || !File.Exists(t.FilePath)) return false;
        if (_metadata == null) return true;
        try
        {
            var e = _metadata.FetchFromLocalFile(t.FilePath);
            return string.IsNullOrWhiteSpace(e.Title) || TrackNormalizer.LooselyMatch(t.Title, e.Title, t.Artist, e.Artist);
        }
        catch { return true; }
    }

    private static HashSet<string> Tokenize(string s) =>
        Regex.Matches(s.ToLowerInvariant(), @"[a-z0-9]+").Select(m => m.Value).Where(w => w.Length > 2).ToHashSet();
}