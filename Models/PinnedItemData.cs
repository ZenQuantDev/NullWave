using System;

namespace NullWave.Models;

/// <summary>
/// Persisted shape of a user-pinned nav item. Mirrors the subset of NavItem
/// that's actually serializable — commands and UI state live only on NavItem.
/// </summary>
public class PinnedItemData
{
    public string Key { get; set; } = string.Empty;
    public NavItemType Type { get; set; }
    public string Label { get; set; } = string.Empty;
    public Guid? TargetPlaylistId { get; set; }
    public string? TargetQuery { get; set; }
}