using System;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.ViewModels.Base;
using Serilog;

namespace NullWave.ViewModels;

public class UserProfileViewModel : ViewModelBase
{
    // ─── Persisted profile record ─────────────────────────────────────────────
    private sealed record ProfileData(
        string Username,
        string Bio,
        string? AvatarPath);

    private string  _username   = "Listener";
    private string  _bio        = "No bio yet";
    private Bitmap? _avatar;

    // ─── Properties ───────────────────────────────────────────────────────────

    public string Username
    {
        get => _username;
        set { _username = value; OnPropertyChanged(); Save(); }
    }

    public string Bio
    {
        get => _bio;
        set { _bio = value; OnPropertyChanged(); Save(); }
    }

    public Bitmap? Avatar
    {
        get => _avatar;
        private set { _avatar = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAvatar)); }
    }

    public bool HasAvatar => _avatar != null;

    // ─── Commands ─────────────────────────────────────────────────────────────

    public ICommand PickAvatarCommand { get; }

    // ─── Constructor ──────────────────────────────────────────────────────────

    public UserProfileViewModel()
    {
        Load();
        PickAvatarCommand = new RelayCommand(async () => await PickAvatarAsync());
    }

    // ─── Persistence ──────────────────────────────────────────────────────────

    private void Load()
    {
        try
        {
            if (!File.Exists(NullWavePaths.ProfilePath)) return;

            var json    = File.ReadAllText(NullWavePaths.ProfilePath);
            var data    = JsonSerializer.Deserialize<ProfileData>(json);
            if (data == null) return;

            _username = data.Username;
            _bio      = data.Bio;

            if (!string.IsNullOrEmpty(data.AvatarPath) &&
                File.Exists(data.AvatarPath))
                Avatar = new Bitmap(data.AvatarPath);

            Log.Debug("[{Source}] Profile loaded: {Username}", nameof(UserProfileViewModel), _username);
        }
        catch (Exception ex)
        {
            NullActionLogger.Error(nameof(UserProfileViewModel), ex, "Profile load failed");
        }
    }

    private void Save()
    {
        try
        {
            var data = new ProfileData(
                Username : _username,
                Bio      : _bio,
                AvatarPath: File.Exists(NullWavePaths.AvatarPath)
                    ? NullWavePaths.AvatarPath : null);

            var json = JsonSerializer.Serialize(data,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(NullWavePaths.ProfilePath, json);
        }
        catch (Exception ex)
        {
            NullActionLogger.Error(nameof(UserProfileViewModel), ex, "Profile save failed");
        }
    }

    private async System.Threading.Tasks.Task PickAvatarAsync()
    {
        try
        {
            var window = Application.Current?.ApplicationLifetime is
                IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow : null;
            if (window == null) return;

            var files = await window.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Choose Profile Picture",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Images")
                        {
                            Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" }
                        }
                    }
                });

            if (files.Count == 0) return;

            var src = files[0].Path.LocalPath;

            // Copy to stable path
            File.Copy(src, NullWavePaths.AvatarPath, overwrite: true);
            Avatar = new Bitmap(NullWavePaths.AvatarPath);
            Save();
            NullActionLogger.SettingChanged("AvatarChanged", nameof(UserProfileViewModel));
        }
        catch (Exception ex)
        {
            NullActionLogger.Error(nameof(UserProfileViewModel), ex, "Avatar pick failed");
        }
    }
}