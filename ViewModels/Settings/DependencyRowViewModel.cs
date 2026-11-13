using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using NullWave.Services;

namespace NullWave.ViewModels.Settings;

public sealed class DependencyRowViewModel : ObservableObject
{
    private readonly Func<string, string> _loc;

    public DependencyRowViewModel(string name, Func<string, string> loc)
    {
        Name = name;
        _loc = loc;
    }

    public string Name { get; }
    public ICommand? ActionCommand { get; set; }
    public DependencyState State { get; private set; }
    public string? Version { get; private set; }
    public string? Latest { get; private set; }
    public string? Note { get; private set; }
    public string? ActionLabel { get; private set; }
    public string? Message { get; private set; }

    public string StatusText => Message ?? DependencyStatus.Text(State, Version, Latest, _loc);
    public bool IsGood => State is DependencyState.UpToDate or DependencyState.Ready;
    public bool IsWarn => State == DependencyState.UpdateAvailable;
    public bool IsBad => State is DependencyState.Missing or DependencyState.Failed;
    public bool ShowAction => ActionCommand != null && !string.IsNullOrEmpty(ActionLabel) && State != DependencyState.Busy;
    public bool ShowNote => !string.IsNullOrEmpty(Note) && !ShowAction;

    public void Update(DependencyState state, string? version = null, string? latest = null,
                       string? actionLabel = null, string? note = null)
    {
        State = state;
        Version = version;
        Latest = latest;
        ActionLabel = actionLabel;
        Note = note;
        Message = null;
        OnPropertyChanged(string.Empty);
    }

    public void SetMessage(string? message)
    {
        Message = message;
        OnPropertyChanged(nameof(StatusText));
    }
}