using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels.Base;
using NullWave.Helpers;
using Serilog;
using System.Collections.ObjectModel;

namespace NullWave.ViewModels;

public class UserProfileViewModel : ViewModelBase, IDisposable
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

    // Cached Stat Fields
    private int _totalTracks;
    private int _totalFavorites;
    private int _totalPlays;
    private int _totalSkips;
    private string _mostPlayedTrack = "-";
    private string? _mostPlayedTrackArtPath;
    private string _mostPlayedArtist = "-";
    private int _youtubeCount;
    private int _soundCloudCount;
    private int _localCount;

    private string? _avatarPath;
    private string _toastMessage = "Saved";

    public System.Collections.ObjectModel.ObservableCollection<NullWave.Models.LiveNotification> ActiveToasts => NullWave.Services.ToastService.Instance.ActiveToasts;

    public string ToastMessage
    {
        get => _toastMessage;
        private set { _toastMessage = value; OnPropertyChanged(); }
    }

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

    public int TotalTracks => _totalTracks;
    public int TotalFavorites => _totalFavorites;
    public int TotalPlays => _totalPlays;
    public int TotalSkips => _totalSkips;
    public string MostPlayedTrack => _mostPlayedTrack;
    public string? MostPlayedTrackArtPath => _mostPlayedTrackArtPath;
    public bool HasMostPlayedTrackArt => !string.IsNullOrEmpty(MostPlayedTrackArtPath);
    public string MostPlayedArtist => _mostPlayedArtist;

    public string MemberSince => _createdAt == default ? DateTime.Now.ToString("MMMM yyyy") : _createdAt.ToString("MMMM yyyy");

    public int YouTubeCount => _youtubeCount;
    public int SoundCloudCount => _soundCloudCount;
    public int LocalCount => _localCount;

    public int YouTubePercentage => TotalTracks > 0 ? (YouTubeCount * 100) / TotalTracks : 0;
    public int SoundCloudPercentage => TotalTracks > 0 ? (SoundCloudCount * 100) / TotalTracks : 0;
    public int LocalPercentage => TotalTracks > 0 ? (LocalCount * 100) / TotalTracks : 0;

    public string YouTubePercentageText => $"{YouTubeCount} ({YouTubePercentage}%)";
    public string SoundCloudPercentageText => $"{SoundCloudCount} ({SoundCloudPercentage}%)";
    public string LocalPercentageText => $"{LocalCount} ({LocalPercentage}%)";

    public ICommand PickAvatarCommand { get; }
    public ICommand ResetAvatarCommand { get; }
    public ICommand ManualSaveCommand { get; }

    private System.Threading.Timer? _saveTimer;

    public UserProfileViewModel(LibraryService? library = null)
    {
        _library = library;
        _createdAt = DateTime.Now;
        
        Load();
        UpdateStatistics();

        _savedUsername = _username;
        _savedBio = _bio;
        _savedHasAvatar = HasAvatar;

        PickAvatarCommand = new RelayCommand(async () => await PickAvatarAsync());
        ResetAvatarCommand = new RelayCommand(ResetAvatar);
        ManualSaveCommand = new RelayCommand(async () => await ManualSaveAsync());

        if (_library != null)
        {
            _library.LibraryChanged += OnLibraryChanged;
        }
    }

    private void OnLibraryChanged(object? sender, EventArgs e)
    {
        UpdateStatistics();
    }

    public void UpdateStatistics()
    {
        if (_library == null) return;

        var allTracks = _library.GetAll();
        if (allTracks == null || !allTracks.Any()) 
        {
            _totalTracks = 0;
            _totalFavorites = 0;
            _totalPlays = 0;
            _totalSkips = 0;
            _mostPlayedTrack = "-";
            _mostPlayedTrackArtPath = null;
            _mostPlayedArtist = "-";
            _youtubeCount = 0;
            _soundCloudCount = 0;
            _localCount = 0;
            RefreshStatProperties();
            return;
        }

        _totalTracks = allTracks.Count;
        _totalFavorites = allTracks.Count(t => t.IsFavorite);
        _totalPlays = allTracks.Sum(t => t.PlayCount);
        _totalSkips = allTracks.Sum(t => t.SkipCount);

        var topTrack = allTracks.OrderByDescending(t => t.PlayCount).FirstOrDefault(t => t.PlayCount > 0);
        _mostPlayedTrack = topTrack?.Title ?? "-";
        _mostPlayedTrackArtPath = topTrack?.AlbumArtPath;

        _mostPlayedArtist = allTracks
            .Where(t => t.Artist != "Unknown" && !string.IsNullOrEmpty(t.Artist))
            .GroupBy(t => t.Artist)
            .OrderByDescending(g => g.Sum(t => t.PlayCount))
            .FirstOrDefault()?.Key ?? "-";

        _youtubeCount = allTracks.Count(t => t.Source == TrackSource.YouTube);
        _soundCloudCount = allTracks.Count(t => t.Source == TrackSource.SoundCloud);
        _localCount = allTracks.Count(t => t.Source == TrackSource.Local);

        RefreshStatProperties();
    }

    private void RefreshStatProperties()
    {
        OnPropertyChanged(nameof(TotalTracks));
        OnPropertyChanged(nameof(TotalFavorites));
        OnPropertyChanged(nameof(TotalPlays));
        OnPropertyChanged(nameof(TotalSkips));
        OnPropertyChanged(nameof(MostPlayedTrack));
        OnPropertyChanged(nameof(MostPlayedArtist));
        OnPropertyChanged(nameof(HasMostPlayedTrackArt));
        OnPropertyChanged(nameof(MostPlayedTrackArtPath));
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

    private void MarkDirty()
    {
        OnPropertyChanged(nameof(IsDirty));
    }

    private void DebouncedSave()
    {
        _saveTimer?.Dispose();
        _saveTimer = new System.Threading.Timer(async _ => 
        {
            await SaveInternalAsync(showToast: false);
        }, null, 2000, System.Threading.Timeout.Infinite);
    }

    private void Load()
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nullwave");
            string filePath = Path.Combine(dir, "profile.json");

            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                var data = JsonSerializer.Deserialize<ProfileData>(json);
                if (data != null)
                {
                    _username = data.Username;
                    _bio = data.Bio;
                    _createdAt = data.CreatedAt;
                    _avatarPath = data.AvatarPath;

                    if (!string.IsNullOrEmpty(_avatarPath) && File.Exists(_avatarPath))
                    {
                        using var stream = File.OpenRead(_avatarPath);
                        Avatar = new Bitmap(stream);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load user profile configuration.");
        }
    }

    private async Task ManualSaveAsync()
    {
        _saveTimer?.Dispose();
        await SaveInternalAsync(showToast: true);
    }

    private async Task SaveInternalAsync(bool showToast)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nullwave");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string filePath = Path.Combine(dir, "profile.json");
            var data = new ProfileData(Username, Bio, _avatarPath, _createdAt);
            
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);

            _savedUsername = Username;
            _savedBio = Bio;
            _savedHasAvatar = HasAvatar;
            
            MarkDirty();

            if (showToast)
            {
                await ShowToastAsync("Saved");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save user profile.");
        }
    }

    private async Task PickAvatarAsync()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow is Window mainWindow)
        {
            var topLevel = TopLevel.GetTopLevel(mainWindow);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Avatar Image",
                FileTypeFilter = new[] { FilePickerFileTypes.ImageAll },
                AllowMultiple = false
            });

            var file = files.FirstOrDefault();
            if (file == null) return;

            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nullwave");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string targetPath = Path.Combine(dir, $"avatar{Path.GetExtension(file.Name)}");

                await using (var sourceStream = await file.OpenReadAsync())
                await using (var targetStream = File.Create(targetPath))
                {
                    await sourceStream.CopyToAsync(targetStream);
                }

                using var stream = File.OpenRead(targetPath);
                Avatar = new Bitmap(stream);
                _avatarPath = targetPath;

                MarkDirty();
                DebouncedSave();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to process selected avatar image.");
            }
        }
    }

    private void ResetAvatar()
    {
        try
        {
            if (!string.IsNullOrEmpty(_avatarPath) && File.Exists(_avatarPath))
            {
                File.Delete(_avatarPath);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not remove avatar asset from storage.");
        }

        Avatar = null;
        _avatarPath = null;
        MarkDirty();
        DebouncedSave();
    }

    public void TriggerExportSuccessToast()
    {
        _ = ShowToastAsync("Profile Card Exported!");
    }

    private async Task ShowToastAsync(string message)
    {
        ToastMessage = message;
        ShowSaveToast = true;
        await Task.Delay(2500);
        ShowSaveToast = false;
    }

    public void Dispose()
    {
        _saveTimer?.Dispose();
        if (_library != null)
        {
            _library.LibraryChanged -= OnLibraryChanged;
        }
    }
}