using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels.Base;
using NullWave.Views;
using Serilog;

namespace NullWave.ViewModels;

public class UserProfileViewModel : ViewModelBase
{
    private sealed record ProfileData(
        string Username,
        string Bio,
        string? AvatarPath,
        DateTime CreatedAt);

    private string _username = "Listener";
    private string _bio = "No bio yet";
    private Bitmap? _avatar;
    private readonly LibraryService? _library;
    private DateTime _createdAt;

    // Dirty state tracking
    private string _savedUsername = "Listener";
    private string _savedBio = "No bio yet";
    private bool _savedHasAvatar;

    // Save toast state
    private bool _showSaveToast;

    public string Username
    {
        get => _username;
        set
        {
            _username = value;
            OnPropertyChanged();
            MarkDirty();
            DebouncedSave();
        }
    }

    public string Bio
    {
        get => _bio;
        set
        {
            _bio = value.Length > 160 ? value[..160] : value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BioLength));
            MarkDirty();
            DebouncedSave();
        }
    }

    public int BioLength => Bio?.Length ?? 0;

    public Bitmap? Avatar
    {
        get => _avatar;
        private set { _avatar = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAvatar)); }
    }

    public bool HasAvatar => _avatar != null;

    public bool IsDirty =>
        _username != _savedUsername ||
        _bio != _savedBio ||
        HasAvatar != _savedHasAvatar;

    public bool ShowSaveToast
    {
        get => _showSaveToast;
        private set { _showSaveToast = value; OnPropertyChanged(); }
    }

    // Real stats from library
    public int TotalTracks => _library?.GetAll().Count ?? 0;
    public int TotalFavorites => _library?.GetAll().Count(t => t.IsFavorite) ?? 0;
    public int TotalPlays => _library?.GetAll().Sum(t => t.PlayCount) ?? 0;

    public string MostPlayedTrack
    {
        get
        {
            var track = _library?.GetAll()
                .OrderByDescending(t => t.PlayCount)
                .FirstOrDefault(t => t.PlayCount > 0);
            return track?.Title ?? "—";
        }
    }

    public string? MostPlayedTrackArtPath
    {
        get
        {
            var track = _library?.GetAll()
                .OrderByDescending(t => t.PlayCount)
                .FirstOrDefault(t => t.PlayCount > 0);
            return track?.AlbumArtPath;
        }
    }

    public bool HasMostPlayedTrackArt => !string.IsNullOrEmpty(MostPlayedTrackArtPath);

    public string MostPlayedArtist
    {
        get
        {
            var artist = _library?.GetAll()
                .Where(t => t.Artist != "Unknown" && !string.IsNullOrEmpty(t.Artist))
                .GroupBy(t => t.Artist)
                .OrderByDescending(g => g.Sum(t => t.PlayCount))
                .FirstOrDefault()?.Key;
            return artist ?? "—";
        }
    }

    public string MemberSince => _createdAt == default ? DateTime.Now.ToString("MMMM yyyy") : _createdAt.ToString("MMMM yyyy");

    public int YouTubeCount => _library?.GetAll().Count(t => t.Source == TrackSource.YouTube) ?? 0;
    public int SoundCloudCount => _library?.GetAll().Count(t => t.Source == TrackSource.SoundCloud) ?? 0;
    public int LocalCount => _library?.GetAll().Count(t => t.Source == TrackSource.Local) ?? 0;

    public int YouTubePercentage => TotalTracks > 0 ? (YouTubeCount * 100) / TotalTracks : 0;
    public int SoundCloudPercentage => TotalTracks > 0 ? (SoundCloudCount * 100) / TotalTracks : 0;
    public int LocalPercentage => TotalTracks > 0 ? (LocalCount * 100) / TotalTracks : 0;

    public string YouTubePercentageText => $"{YouTubeCount} ({YouTubePercentage}%)";
    public string SoundCloudPercentageText => $"{SoundCloudCount} ({SoundCloudPercentage}%)";
    public string LocalPercentageText => $"{LocalCount} ({LocalPercentage}%)";

    public ICommand PickAvatarCommand { get; }
    public ICommand ResetAvatarCommand { get; }
    public ICommand ExportProfileCommand { get; }
    public ICommand ManualSaveCommand { get; }

    private System.Threading.Timer? _saveTimer;

    public UserProfileViewModel(LibraryService? library = null)
    {
        _library = library;
        _createdAt = DateTime.Now;
        Load();

        _savedUsername = _username;
        _savedBio = _bio;
        _savedHasAvatar = HasAvatar;

        PickAvatarCommand = new RelayCommand(async () => await PickAvatarAsync());
        ResetAvatarCommand = new RelayCommand(ResetAvatar);
        ExportProfileCommand = new RelayCommand(async () => await ExportProfileAsync());
        ManualSaveCommand = new RelayCommand(async () => await ManualSaveAsync());
    }

    private void MarkDirty()
    {
        OnPropertyChanged(nameof(IsDirty));
    }

    private void DebouncedSave()
    {
        _saveTimer?.Dispose();
        _saveTimer = new System.Threading.Timer(async _ =>
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await SaveAsync(showToast: false);
            });
        }, null, 800, System.Threading.Timeout.Infinite);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(NullWavePaths.ProfilePath)) return;

            var json = File.ReadAllText(NullWavePaths.ProfilePath);
            var data = JsonSerializer.Deserialize<ProfileData>(json);
            if (data == null) return;

            _username = data.Username;
            _bio = data.Bio;
            _createdAt = data.CreatedAt == default ? DateTime.Now : data.CreatedAt;

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

    private async Task SaveAsync(bool showToast = true)
    {
        try
        {
            var data = new ProfileData(
                Username: _username,
                Bio: _bio,
                AvatarPath: File.Exists(NullWavePaths.AvatarPath) ? NullWavePaths.AvatarPath : null,
                CreatedAt: _createdAt == default ? DateTime.Now : _createdAt);

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(NullWavePaths.ProfilePath, json);

            _savedUsername = _username;
            _savedBio = _bio;
            _savedHasAvatar = HasAvatar;
            OnPropertyChanged(nameof(IsDirty));

            if (showToast)
            {
                ShowSaveToast = true;
                await Task.Delay(1800);
                ShowSaveToast = false;
            }

            OnPropertyChanged(nameof(TotalTracks));
            OnPropertyChanged(nameof(TotalFavorites));
            OnPropertyChanged(nameof(TotalPlays));
            OnPropertyChanged(nameof(MostPlayedTrack));
            OnPropertyChanged(nameof(MostPlayedArtist));
            OnPropertyChanged(nameof(YouTubeCount));
            OnPropertyChanged(nameof(SoundCloudCount));
            OnPropertyChanged(nameof(LocalCount));
            OnPropertyChanged(nameof(YouTubePercentage));
            OnPropertyChanged(nameof(SoundCloudPercentage));
            OnPropertyChanged(nameof(LocalPercentage));
            OnPropertyChanged(nameof(YouTubePercentageText));
            OnPropertyChanged(nameof(SoundCloudPercentageText));
            OnPropertyChanged(nameof(LocalPercentageText));
        }
        catch (Exception ex)
        {
            NullActionLogger.Error(nameof(UserProfileViewModel), ex, "Profile save failed");
        }
    }

    private async Task ManualSaveAsync() => await SaveAsync(showToast: true);

    private async Task PickAvatarAsync()
    {
        try
        {
            var window = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
            if (window == null) return;

            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose Profile Picture",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" } } }
            });

            if (files.Count == 0) return;

            File.Copy(files[0].Path.LocalPath, NullWavePaths.AvatarPath, overwrite: true);
            Avatar = new Bitmap(NullWavePaths.AvatarPath);
            await SaveAsync(showToast: true);
        }
        catch (Exception ex)
        {
            NullActionLogger.Error(nameof(UserProfileViewModel), ex, "Avatar pick failed");
        }
    }

    private async void ResetAvatar()
    {
        try
        {
            if (File.Exists(NullWavePaths.AvatarPath)) File.Delete(NullWavePaths.AvatarPath);
            Avatar = null;
            await SaveAsync(showToast: true);
        }
        catch (Exception ex)
        {
            NullActionLogger.Error(nameof(UserProfileViewModel), ex, "Avatar reset failed");
        }
    }

    private async Task ExportProfileAsync()
    {
        try
        {
            var window = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.Windows.OfType<ProfileWindow>().FirstOrDefault() : null;

            if (window == null) return;

            var path = await window.ExportProfileCardAsync();
            if (path != null)
            {
                ShowSaveToast = true;
                await Task.Delay(2500);
                ShowSaveToast = false;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Profile] Export failed");
        }
    }
}