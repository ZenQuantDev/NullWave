using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Serilog;
using NullWave.Models;
using NullWave.Services;
using NullWave.Services.Security;
using NullWave.Helpers;
using NullWave.ViewModels.Base;

namespace NullWave.ViewModels;

public partial class UserProfileViewModel : ViewModelBase, IDisposable
{
    private sealed record ProfileData(string Username, string Bio, string? AvatarPath, string? BannerImagePath, string? InstallId, DateTime CreatedAt);
    public sealed record ProfileBadge(string Id, MaterialIconKind Icon, string Name, string Description, IBrush ColorHex);
    public sealed record TrackTag(string Tag, int Count);
    public sealed record ArtistStat(string Name, int PlayCount);

    private readonly LibraryService? _library;
    private readonly PreferencesService _prefs;
    private readonly IdentityService _identity;
    private readonly PropertyChangedEventHandler _langHandler;
    private readonly string _badgesPath;
    private List<SignedBadge> _grantedBadges = new();

    private string _username = "Listener";
    private string _bio = "No bio yet";
    private Bitmap? _avatar;
    private DateTime _createdAt;
    private string _savedUsername = "Listener";
    private string _savedBio = "No bio yet";
    private bool _savedHasAvatar;
    private string? _savedAvatarPath;
    private string? _savedBannerImagePath;
    private bool _showSaveToast;
    private string _toastMessage = "Saved";
    private string _installId = string.Empty;
    private string? _avatarPath;
    private string? _bannerImagePath;
    private TimeSpan _totalListeningTime;
    private int _totalTracks;
    private int _totalFavorites;
    private int _totalPlays;
    private int _totalSkips;
    private string _mostPlayedTrack = "-";
    private Guid? _mostPlayedTrackId;
    private string? _mostPlayedTrackArtPath;
    private string _mostPlayedArtist = "-";
    private int _youtubeCount;
    private int _soundCloudCount;
    private int _localCount;
    private string _longestTrack = "-";
    private string _shortestTrack = "-";
    private string _averageTrackLength = "0:00";
    private int _currentStreak;
    private int _longestStreak;
    private Dictionary<string, double> _tasteDistribution = new();
    private Dictionary<string, double> _moodDistribution = new();
    private System.Threading.Timer? _saveTimer;
    private string _shareCode = "";
    private Bitmap? _shareQr;

    public ObservableCollection<ProfileBadge> Badges { get; } = new();
    public bool HasBadges => Badges.Count > 0;
    public ObservableCollection<TrackTag> TopTags { get; } = new();
    public bool HasTopTags => TopTags.Count > 0;
    public ObservableCollection<ArtistStat> TopArtists { get; } = new();
    public bool HasTopArtists => TopArtists.Count > 0;
    public ObservableCollection<Track> RecentlyPlayed { get; } = new();
    public bool HasRecentlyPlayed => RecentlyPlayed.Count > 0;
    public bool HasTopTrack => _mostPlayedTrackId != null;
    public ObservableCollection<LiveNotification> ActiveToasts => ToastService.Instance.ActiveToasts;

    public event Action<string>? TagClickedRequested;
    public event Action<Guid>? PlayTrackByIdRequested;

    public string TotalListeningTimeDisplay => DurationFormatter.FormatListeningTime(_totalListeningTime);
    public string LongestTrack => _longestTrack;
    public string ShortestTrack => _shortestTrack;
    public string AverageTrackLength => _averageTrackLength;
    public int CurrentStreak => _currentStreak;
    public int LongestStreak => _longestStreak;
    public Dictionary<string, double> TasteDistribution => _tasteDistribution;
    public Dictionary<string, double> MoodDistribution => _moodDistribution;

    public UserProfileViewModel(LibraryService? library, PreferencesService prefs, IdentityService identity)
    {
        _library = library;
        _prefs = prefs;
        _identity = identity;
        _badgesPath = Path.Combine(NullWavePaths.DataDir, "badges.json");
        Load();
        _savedUsername = _username; _savedBio = _bio; _savedHasAvatar = HasAvatar; _savedAvatarPath = _avatarPath; _savedBannerImagePath = _bannerImagePath;
        
        InitializeCommands();

        if (_library != null) _library.LibraryChanged += OnLibraryChanged;
        _langHandler = (_, _) => Dispatcher.UIThread.Post(RefreshBadges);
        LocalizationService.Instance.PropertyChanged += _langHandler;
        Dispatcher.UIThread.Post(UpdateStatistics);
    }

    private bool _isSavingChanges;
    public bool IsSavingChanges { get => _isSavingChanges; private set { _isSavingChanges = value; OnPropertyChanged(); } }
    private bool _isEditorOpen;
    public bool IsEditorOpen { get => _isEditorOpen; set { _isEditorOpen = value; OnPropertyChanged(); } }

    public string BannerColor { get => _prefs.Current.ProfileBannerColor; set { _prefs.Update(p => p.ProfileBannerColor = value); OnPropertyChanged(); OnPropertyChanged(nameof(IsCustomBanner)); OnPropertyChanged(nameof(IsDefaultBanner)); } }
    public string? BannerImagePath { get => _bannerImagePath; set { _bannerImagePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasBannerImage)); OnPropertyChanged(nameof(IsCustomBanner)); OnPropertyChanged(nameof(IsDefaultBanner)); } }
    public bool HasBannerImage => !string.IsNullOrEmpty(BannerImagePath);
    public bool IsCustomBanner => !HasBannerImage && !string.IsNullOrEmpty(BannerColor) && BannerColor != "accent";
    public bool IsDefaultBanner => !HasBannerImage && !IsCustomBanner;
    public string ProfileFrameStyle { get => _prefs.Current.ProfileFrameStyle; set { _prefs.Update(p => p.ProfileFrameStyle = value); OnPropertyChanged(); ThemeService.Instance.ApplyProfileFrame(value); DebouncedSave(); } }
    public bool TrackSharingEnabled { get => _prefs.Current.EnableTrackSharing; set { _prefs.Update(p => p.EnableTrackSharing = value); OnPropertyChanged(); DebouncedSave(); } }
    public string InstallId => _installId;
    public string ToastMessage { get => _toastMessage; private set { _toastMessage = value; OnPropertyChanged(); } }
    public string Username { get => _username; set { _username = value; OnPropertyChanged(); MarkDirty(); DebouncedSave(); } }
    public string Bio { get => _bio; set { _bio = value.Length > 160 ? value[..160] : value; OnPropertyChanged(); OnPropertyChanged(nameof(BioLength)); MarkDirty(); DebouncedSave(); } }
    public int BioLength => Bio?.Length ?? 0;
    public Bitmap? Avatar { get => _avatar; private set { _avatar = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAvatar)); } }
    public bool HasAvatar => _avatar != null;
    public bool IsDirty => _username != _savedUsername || _bio != _savedBio || HasAvatar != _savedHasAvatar || _avatarPath != _savedAvatarPath || _bannerImagePath != _savedBannerImagePath;
    public bool ShowSaveToast { get => _showSaveToast; private set { _showSaveToast = value; OnPropertyChanged(); } }
    
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
    public string ShareCode => _shareCode;
    public Bitmap? ShareQrBitmap => _shareQr;
    public string ShareString => ProfileShareService.Encode(BuildSharePayload());
    public ProfileSharePayload? LastGuest { get; private set; }
    public double GuestOverlap { get; private set; }
    public string GuestOverlapText => LastGuest == null ? "" : $"You share {(int)(GuestOverlap * 100)}% taste with {LastGuest.Name}";

    public ICommand PickAvatarCommand { get; private set; } = null!;
    public ICommand ResetAvatarCommand { get; private set; } = null!;
    public ICommand ManualSaveCommand { get; private set; } = null!;
    public ICommand ToggleEditorCommand { get; private set; } = null!;
    public ICommand OpenEditorCommand { get; private set; } = null!;
    public ICommand CloseEditorCommand { get; private set; } = null!;
    public ICommand SetBannerColorCommand { get; private set; } = null!;
    public ICommand PickBannerImageCommand { get; private set; } = null!;
    public ICommand ClearBannerImageCommand { get; private set; } = null!;
    public ICommand SetProfileFrameCommand { get; private set; } = null!;
    public ICommand FilterByTagCommand { get; private set; } = null!;
    public ICommand PlayTopTrackCommand { get; private set; } = null!;
    public ICommand CopyInstallIdCommand { get; private set; } = null!;
    public ICommand CopyShareStringCommand { get; private set; } = null!;
    public ICommand ImportShareStringCommand { get; private set; } = null!;
    public ICommand QueueMatchingTracksCommand { get; private set; } = null!;

    private void Load()
    {
        try
        {
            string dir = NullWavePaths.DataDir;
            string filePath = Path.Combine(dir, "profile.json");
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                var data = JsonSerializer.Deserialize<ProfileData>(json);
                if (data != null)
                {
                    _username = data.Username; _bio = data.Bio; _createdAt = data.CreatedAt;
                    var rawAvatar = data.AvatarPath;
                    if (!string.IsNullOrEmpty(rawAvatar)) _avatarPath = Path.IsPathRooted(rawAvatar) && File.Exists(rawAvatar) ? rawAvatar : Path.Combine(GetProfileAssetsDirectory(), Path.GetFileName(rawAvatar));
                    var rawBanner = data.BannerImagePath;
                    if (!string.IsNullOrEmpty(rawBanner)) _bannerImagePath = Path.IsPathRooted(rawBanner) && File.Exists(rawBanner) ? rawBanner : Path.Combine(GetProfileAssetsDirectory(), Path.GetFileName(rawBanner));
                    _installId = data.InstallId ?? string.Empty;
                }
            }
            try { if (File.Exists(_badgesPath)) _grantedBadges = JsonSerializer.Deserialize<List<SignedBadge>>(File.ReadAllText(_badgesPath)) ?? new(); }
            catch (Exception ex) { Log.Warning(ex, "Failed to load granted badges."); }
            if (string.IsNullOrEmpty(_installId)) { _installId = _identity.Fingerprint; _ = SaveInternalAsync(showToast: false); }
            if (!string.IsNullOrEmpty(_avatarPath) && File.Exists(_avatarPath))
            {
                using var fileStream = File.OpenRead(_avatarPath);
                var memoryStream = new MemoryStream();
                fileStream.CopyTo(memoryStream);
                memoryStream.Position = 0;
                Avatar = new Bitmap(memoryStream);
            }
        }
        catch (Exception ex) { Log.Error(ex, "Failed to load user profile configuration."); }
    }

    private async Task ManualSaveAsync() { _saveTimer?.Dispose(); IsSavingChanges = true; await SaveInternalAsync(showToast: true); }

    private async Task SaveInternalAsync(bool showToast)
    {
        try
        {
            string dir = NullWavePaths.DataDir;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string filePath = Path.Combine(dir, "profile.json");
            var data = new ProfileData(Username, Bio, string.IsNullOrEmpty(_avatarPath) ? null : Path.GetFileName(_avatarPath), string.IsNullOrEmpty(_bannerImagePath) ? null : Path.GetFileName(_bannerImagePath), _installId, _createdAt);
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);
            _savedUsername = Username; _savedBio = Bio; _savedHasAvatar = HasAvatar; _savedAvatarPath = _avatarPath; _savedBannerImagePath = _bannerImagePath;
            MarkDirty();
            if (showToast) { await Dispatcher.UIThread.InvokeAsync(() => ShowSaveToast = true); await Task.Delay(2500); await Dispatcher.UIThread.InvokeAsync(() => ShowSaveToast = false); }
        }
        catch (Exception ex) { Log.Error(ex, "Failed to save user profile."); }
        finally { await Dispatcher.UIThread.InvokeAsync(() => IsSavingChanges = false); }
    }

    private void MarkDirty() => OnPropertyChanged(nameof(IsDirty));

    private void DebouncedSave()
    {
        Dispatcher.UIThread.Post(() => IsSavingChanges = true);
        _saveTimer?.Dispose();
        _saveTimer = new System.Threading.Timer(async _ => await SaveInternalAsync(showToast: false), null, 2000, System.Threading.Timeout.Infinite);
    }

    private static string GetProfileAssetsDirectory() => Path.Combine(NullWavePaths.DataDir, "profile-assets");

    private static void TryDeleteProfileAsset(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            string fullPath = PathHelper.Resolve(path) ?? path;
            string assetsDirectory = Path.GetFullPath(GetProfileAssetsDirectory()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(fullPath);
            if (File.Exists(candidate) && candidate.StartsWith(assetsDirectory, StringComparison.OrdinalIgnoreCase)) File.Delete(candidate);
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to delete old profile asset: {Path}", path); }
    }

    public void Dispose()
    {
        _saveTimer?.Dispose();
        LocalizationService.Instance.PropertyChanged -= _langHandler;
        if (_library != null) _library.LibraryChanged -= OnLibraryChanged;
    }
}