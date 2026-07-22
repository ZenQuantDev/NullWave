using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NullWave.Models;

public enum NavItemType { Core, PinnedPlaylist, SavedSearch }

/// <summary>
/// A single entry in the sidebar's reorderable nav list. Key is a stable
/// identifier used for persisting order and active-state matching — never
/// shown to the user, unlike Label. Core items use their page name as Key
/// (matches MainViewModel.CurrentPage). Pinned items use "pin:{playlistId}"
/// or "search:{query}" so they stay stable across app restarts even though
/// they're dynamically created/removed by the user.
/// </summary>
public partial class NavItem : ObservableObject
{
    public string Key { get; }
    public NavItemType Type { get; }
    public Guid? TargetPlaylistId { get; }
    public string? TargetQuery { get; }

    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private string _iconKind = string.Empty;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isAutoSuggested;

    public bool CanUnpin => Type != NavItemType.Core;

    public ICommand? Command { get; set; }

    public NavItem(string key, string label, string iconKind,
        NavItemType type = NavItemType.Core, Guid? targetPlaylistId = null, string? targetQuery = null)
    {
        Key = key;
        Label = label;
        IconKind = iconKind;
        Type = type;
        TargetPlaylistId = targetPlaylistId;
        TargetQuery = targetQuery;
    }
}