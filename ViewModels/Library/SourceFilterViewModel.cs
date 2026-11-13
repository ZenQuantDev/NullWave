using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;
using NullWave.Models;

namespace NullWave.ViewModels;

public partial class SourceFilterViewModel : ObservableObject
{
    [ObservableProperty] private int _trackCount;
    [ObservableProperty] private bool _isActive;

    public string DisplayName { get; }
    public TrackSource Source { get; }
    public MaterialIconKind IconKind { get; }
    public string IconColor { get; }
    public ICommand FilterCommand { get; }

    public SourceFilterViewModel(
        string displayName, 
        TrackSource source, 
        MaterialIconKind iconKind, 
        string iconColor,
        ICommand filterCommand)
    {
        DisplayName = displayName;
        Source = source;
        IconKind = iconKind;
        IconColor = iconColor;
        FilterCommand = filterCommand;
    }
}