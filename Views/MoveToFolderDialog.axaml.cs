using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Material.Icons;
using NullWave.Services;

namespace NullWave.Views;

public class FolderOption
{
    public Guid? FolderId { get; set; }
    public string Name { get; set; } = "";
    public MaterialIconKind Icon { get; set; } = MaterialIconKind.Folder;
}

public partial class MoveToFolderDialog : Window
{
    public MoveToFolderDialog() { InitializeComponent(); }

    public MoveToFolderDialog(PlaylistService playlists, Guid? currentFolderId) : this()
    {
        var options = new ObservableCollection<FolderOption>
        {
            new() { FolderId = null, Name = "Top level (no folder)", Icon = MaterialIconKind.FolderOpenOutline }
        };
        foreach (var f in playlists.GetAllFolders().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            options.Add(new FolderOption { FolderId = f.Id, Name = f.Name });

        FolderList.ItemsSource = options;
        FolderList.SelectedItem = options.FirstOrDefault(o => o.FolderId == currentFolderId) ?? options[0];
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(FolderList.SelectedItem as FolderOption);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}