using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NullWave.Models;
using NullWave.Services;
using System;

namespace NullWave.ViewModels.Dialogs;

public partial class DownloaderConsentViewModel : ObservableObject
{
    private readonly PreferencesService _prefs;
    private readonly Action _onAccepted;
    private readonly Action _onDeclined;
    public bool CanAccept => UserUnderstands && UserAcceptsResponsibility;

    [ObservableProperty] private bool _userUnderstands;
    [ObservableProperty] private bool _userAcceptsResponsibility;

    public DownloaderConsentViewModel(PreferencesService prefs, Action onAccepted, Action onDeclined)
    {
        _prefs = prefs;
        _onAccepted = onAccepted;
        _onDeclined = onDeclined;
    }

    [RelayCommand]
    private void Accept()
    {
        var consent = new DownloaderConsent
        {
            AcceptedVersion = DownloaderConsent.CurrentVersion,
            AcceptedAtUtc = DateTime.UtcNow
        };
        _prefs.Update(p => p.DownloaderConsent = consent);
        _onAccepted();
    }

    [RelayCommand]
    private void Decline()
    {
        _onDeclined();
    }
}