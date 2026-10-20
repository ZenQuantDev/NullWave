using System;
using System.Linq;

namespace NullWave.Models;

/// <summary>
/// Single source of truth for which sort fields are valid on each page.
/// Stops the toolbar dropdown from offering no-op choices and stops a page from
/// inheriting an option it cannot handle (e.g. the Library receiving the
/// Playlist's manual "Custom" order after a page switch).
/// Phase 7.2 (shared sort factory) builds on this catalog.
/// </summary>
public static class SortFieldCatalog
{
    /// <summary>Library, Radio and Audiobooks. "Custom" is meaningless here.</summary>
    public static readonly SortField[] Library =
    {
        SortField.Title, SortField.Artist, SortField.DateAdded,
        SortField.Source, SortField.PlayCount, SortField.LastPlayed, SortField.Length
    };

    /// <summary>Playlist page. Custom first: it is the default and enables drag reorder.</summary>
    public static readonly SortField[] Playlist =
    {
        SortField.Custom, SortField.Title, SortField.Artist, SortField.DateAdded,
        SortField.Source, SortField.PlayCount, SortField.LastPlayed, SortField.Length
    };

    public static SortField[] ForPage(string? page) =>
        string.Equals(page, "Playlists", StringComparison.OrdinalIgnoreCase) ? Playlist : Library;

    public static bool IsAllowed(string? page, SortField field) => ForPage(page).Contains(field);

    /// <summary>Returns <paramref name="field"/> when the page supports it, otherwise the page's first valid option.</summary>
    public static SortField Clamp(string? page, SortField field)
    {
        var allowed = ForPage(page);
        return allowed.Contains(field) ? field : allowed[0];
    }
}