using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace NullWave.ViewModels;

public partial class SettingsViewModel
{
    public event Func<Task>? PreviewRadioHealRequested;
    public event Func<Task>? RepairRadioHealRequested;

    public bool AutoHealRadioStreams
    {
        get => _prefsService.Current.AutoHealRadioStreams;
        set
        {
            _prefsService.Update(p => p.AutoHealRadioStreams = value);
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private async Task PreviewRadioHealAsync()
    {
        IsRepairing = true;
        try
        {
            if (PreviewRadioHealRequested != null)
                await PreviewRadioHealRequested();
        }
        finally
        {
            IsRepairing = false;
        }
    }

    [RelayCommand]
    private async Task RepairRadioHealAsync()
    {
        IsRepairing = true;
        try
        {
            if (RepairRadioHealRequested != null)
                await RepairRadioHealRequested();
        }
        finally
        {
            IsRepairing = false;
        }
    }
}
