using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace NullWave.Models;

public enum NavItemType { Core, PinnedPlaylist, SavedSearch }

public partial class NavItem : ObservableObject
{
    public string Key { get; }
    public NavItemType Type { get; }
    public Guid? TargetPlaylistId { get; }
    public string? TargetQuery { get; }

    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private MaterialIconKind _iconKind;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isAutoSuggested;
    [ObservableProperty] private bool _isDragging;
    [ObservableProperty] private bool _isDropTarget;
    [ObservableProperty] private string? _artPath;
    [ObservableProperty] private string? _subtitle;
    [ObservableProperty] private Playlist? _playlist;

    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);
    public bool CanUnpin => Type != NavItemType.Core;

    public ICommand? Command { get; set; }

    public NavItem(string key, string label, MaterialIconKind iconKind,
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

/// <summary>Expandable folder node for the sidebar tree.</summary>
public partial class SidebarFolderNode : ObservableObject
{
    public PlaylistFolder Folder { get; }
    public ObservableCollection<Playlist> Playlists { get; } = new();

    [ObservableProperty] private bool _isExpanded = true;

    public SidebarFolderNode(PlaylistFolder folder)
    {
        Folder = folder;
        _isExpanded = folder.IsExpanded;
    }
}