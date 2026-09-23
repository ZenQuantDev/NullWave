using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using NullWave.Services.Security;
using NullWave.ViewModels.Base;
using Serilog;

namespace NullWave.ViewModels;

public partial class UserProfileViewModel : ViewModelBase, IDisposable
{
    private sealed record ProfileData(
        string Username,
        string Bio,
        string? AvatarPath,
        string? BannerImagePath,
        string? InstallId,
        DateTime CreatedAt);

    public sealed record ProfileBadge(string Id, MaterialIconKind Icon, string Name, string Description, IBrush ColorHex);
    public sealed record TrackTag(string Tag, int Count);
    public sealed record ArtistStat(string Name, int PlayCount);

    private readonly LibraryService? _library;
    private readonly PreferencesService _prefs;
    private readonly IdentityService _identity;
    private readonly PropertyChangedEventHandler _langHandler;
    
    // FIX: Route through NullWavePaths so NULLWAVE_HOME is respected during tests and factory resets
    private readonly string _badgesPath;
    private List<SignedBadge> _grantedBadges = new();

    public ObservableCollection<ProfileBadge> Badges { get; } = new();
    public bool HasBadges => Badges.Count > 0;

    public ObservableCollection<TrackTag> TopTags { get; } = new();
    public bool HasTopTags => TopTags.Count > 0;

    public ObservableCollection<ArtistStat> TopArtists { get; } = new();
    public bool HasTopArtists => TopArtists.Count > 0;

    public event Action<string>? TagClickedRequested;
    public event Action<Guid>? PlayTrackByIdRequested;

    [RelayCommand] private void FilterByTag(string? tag)
    {
        if (!string.IsNullOrEmpty(tag)) TagClickedRequested?.Invoke(tag);
    }

    [RelayCommand] private void PlayTopTrack()
    {
        if (_mostPlayedTrackId is { } id) PlayTrackByIdRequested?.Invoke(id);
    }

    [RelayCommand]
    private async Task CopyInstallIdAsync()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(InstallId);
                ToastService.Instance.Show(L("Profile_Account_CopyId_Done"), ToastType.Success);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to copy Install ID to clipboard.");
        }
    }

    private static string L(string key) => LocalizationService.Instance[key];

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

    public ObservableCollection<Track> RecentlyPlayed { get; } = new();
    public bool HasRecentlyPlayed => RecentlyPlayed.Count > 0;
    public bool HasTopTrack => _mostPlayedTrackId != null;

    public ObservableCollection<LiveNotification> ActiveToasts => ToastService.Instance.ActiveToasts;
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

        _savedUsername = _username;
        _savedBio = _bio;
        _savedHasAvatar = HasAvatar;
        _savedAvatarPath = _avatarPath;
        _savedBannerImagePath = _bannerImagePath;

        PickAvatarCommand = new NullWave.Helpers.RelayCommand(async () => await PickAvatarAsync());
        ResetAvatarCommand = new NullWave.Helpers.RelayCommand(ResetAvatar);
        ManualSaveCommand = new NullWave.Helpers.RelayCommand(async () => await ManualSaveAsync());

        if (_library != null)
            _library.LibraryChanged += OnLibraryChanged;

        _langHandler = (_, _) => Dispatcher.UIThread.Post(RefreshBadges);
        LocalizationService.Instance.PropertyChanged += _langHandler;

        Dispatcher.UIThread.Post(UpdateStatistics);
    }

    private void OnLibraryChanged(object? sender, EventArgs e) 
    {
        Dispatcher.UIThread.Post(UpdateStatistics);
    }

    private bool _isSavingChanges;
    public bool IsSavingChanges
    {
        get => _isSavingChanges;
        private set { _isSavingChanges = value; OnPropertyChanged(); }
    }

    private bool _isEditorOpen;
    public bool IsEditorOpen
    {
        get => _isEditorOpen;
        set { _isEditorOpen = value; OnPropertyChanged(); }
    }
    [global::CommunityToolkit.Mvvm.Input.RelayCommand] private void ToggleEditor() => IsEditorOpen = !IsEditorOpen;
    [global::CommunityToolkit.Mvvm.Input.RelayCommand] private void OpenEditor() => IsEditorOpen = true;
    [global::CommunityToolkit.Mvvm.Input.RelayCommand] private void CloseEditor() => IsEditorOpen = false;

    public string BannerColor
    {
        get => _prefs.Current.ProfileBannerColor;
        set
        {
            _prefs.Update(p => p.ProfileBannerColor = value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCustomBanner));
            OnPropertyChanged(nameof(IsDefaultBanner));
        }
    }
    public string? BannerImagePath
    {
        get => _bannerImagePath;
        set
        {
            _bannerImagePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasBannerImage));
            OnPropertyChanged(nameof(IsCustomBanner));
            OnPropertyChanged(nameof(IsDefaultBanner));
        }
    }
    public bool HasBannerImage => !string.IsNullOrEmpty(BannerImagePath);
    public bool IsCustomBanner => !HasBannerImage && !string.IsNullOrEmpty(BannerColor) && BannerColor != "accent";
    public bool IsDefaultBanner => !HasBannerImage && !IsCustomBanner;

    [global::CommunityToolkit.Mvvm.Input.RelayCommand] private void SetBannerColor(string color) => BannerColor = color;

    [global::CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task PickBannerImageAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return;
        var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Banner Image",
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll },
            AllowMultiple = false
        });
        var file = files.FirstOrDefault();
        if (file == null) return;

        string? oldBannerPath = BannerImagePath;
        try
        {
            string dir = GetProfileAssetsDirectory();
            Directory.CreateDirectory(dir);
            string extension = Path.GetExtension(file.Name);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".png";
            string targetPath = Path.Combine(dir, $"banner_{DateTime.Now:yyyyMMdd_HHmmss_fff}{extension}");

            await using (var src = await file.OpenReadAsync())
            await using (var dst = File.Create(targetPath))
                await src.CopyToAsync(dst);

            BannerImagePath = targetPath;
            await SaveInternalAsync(showToast: false);
            TryDeleteProfileAsset(oldBannerPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to set banner image.");
            ToastService.Instance.Show(L("Profile_Badge_Invalid"), ToastType.Error);
        }
    }

    [global::CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task ClearBannerImageAsync()
    {
        string? oldBannerPath = BannerImagePath;
        BannerImagePath = null;
        await SaveInternalAsync(showToast: false);
        TryDeleteProfileAsset(oldBannerPath);
    }

    public string ProfileFrameStyle
    {
        get => _prefs.Current.ProfileFrameStyle;
        set
        {
            _prefs.Update(p => p.ProfileFrameStyle = value);
            OnPropertyChanged();
            ThemeService.Instance.ApplyProfileFrame(value);
            DebouncedSave();
        }
    }
    [global::CommunityToolkit.Mvvm.Input.RelayCommand] private void SetProfileFrame(string style) => ProfileFrameStyle = style;

    public bool TrackSharingEnabled
    {
        get => _prefs.Current.EnableTrackSharing;
        set
        {
            _prefs.Update(p => p.EnableTrackSharing = value);
            OnPropertyChanged();
            DebouncedSave();
        }
    }

    public string InstallId => _installId;

    public string ToastMessage
    {
        get => _toastMessage;
        private set { _toastMessage = value; OnPropertyChanged(); }
    }

    public string Username
    {
        get => _username;
        set { _username = value; OnPropertyChanged(); MarkDirty(); DebouncedSave(); }
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
        HasAvatar != _savedHasAvatar ||
        _avatarPath != _savedAvatarPath ||
        _bannerImagePath != _savedBannerImagePath;

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

    public void ImportBadge(string json)
    {
        try
        {
            var badge = JsonSerializer.Deserialize<SignedBadge>(json);
            
            if (badge == null || !badge.VerifyAgainstMasterKey() || !badge.IsOfficial())
            {
                ToastService.Instance.Show(L("Profile_Badge_Invalid"), ToastType.Error);
                return;
            }

            if (!badge.IsBoundTo(_identity.Fingerprint))
            {
                ToastService.Instance.Show(L("Profile_Badge_NotYours"), ToastType.Warning);
                return;
            }

            if (_grantedBadges.Any(b => b.Payload.BadgeType == badge.Payload.BadgeType))
            {
                ToastService.Instance.Show(L("Profile_Badge_AlreadyOwned"), ToastType.Info);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_badgesPath)!);
            _grantedBadges.Add(badge);
            File.WriteAllText(_badgesPath, JsonSerializer.Serialize(_grantedBadges));
            Dispatcher.UIThread.Post(RefreshBadges);
            ToastService.Instance.Show(L("Profile_Badge_Unlocked"), ToastType.Success);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Badge import failed");
            ToastService.Instance.Show(L("Profile_Badge_Invalid"), ToastType.Error);
        }
    }

    public void UpdateStatistics()
    {
        if (_library == null) return;

        var allTracks = _library.GetAll();
        if (allTracks == null || !allTracks.Any())
        {
            _totalTracks = 0; _totalFavorites = 0; _totalPlays = 0; _totalSkips = 0;
            _mostPlayedTrack = "-"; _mostPlayedTrackId = null; _mostPlayedTrackArtPath = null; _mostPlayedArtist = "-";
            _youtubeCount = 0; _soundCloudCount = 0; _localCount = 0;
            _totalListeningTime = TimeSpan.Zero;
            _longestTrack = "-"; _shortestTrack = "-"; _averageTrackLength = "0:00";
            _currentStreak = 0; _longestStreak = 0;
            _tasteDistribution = TagTaxonomy.GenreAxes.Keys.ToDictionary(k => k, _ => 0.0);
            _moodDistribution = TagTaxonomy.MoodAxes.Keys.ToDictionary(k => k, _ => 0.0);
            RecentlyPlayed.Clear();
            TopTags.Clear();
            TopArtists.Clear();
            RefreshStatProperties();
            RefreshBadges();
            return;
        }

        _totalTracks = allTracks.Count;
        _totalFavorites = allTracks.Count(t => t.IsFavorite);
        _totalPlays = allTracks.Sum(t => t.PlayCount);
        _totalSkips = allTracks.Sum(t => t.SkipCount);

        var topTrack = allTracks.OrderByDescending(t => t.PlayCount).FirstOrDefault(t => t.PlayCount > 0);
        _mostPlayedTrack = topTrack?.Title ?? "-";
        _mostPlayedTrackId = topTrack?.Id;
        _mostPlayedTrackArtPath = topTrack?.AlbumArtPath;

        _mostPlayedArtist = allTracks
            .Where(t => t.Artist != "Unknown" && !string.IsNullOrEmpty(t.Artist))
            .GroupBy(t => t.Artist)
            .OrderByDescending(g => g.Sum(t => t.PlayCount))
            .FirstOrDefault()?.Key ?? "-";

        _youtubeCount = allTracks.Count(t => t.Source == TrackSource.YouTube);
        _soundCloudCount = allTracks.Count(t => t.Source == TrackSource.SoundCloud);
        _localCount = allTracks.Count(t => t.Source == TrackSource.Local);

        _totalListeningTime = TimeSpan.FromTicks(allTracks.Sum(t => t.Duration.Ticks * (long)t.PlayCount));

        var validDurations = allTracks.Where(t => t.Duration > TimeSpan.Zero).ToList();
        if (validDurations.Any())
        {
            var longest = validDurations.OrderByDescending(t => t.Duration).First();
            _longestTrack = $"{longest.Title} ({longest.Duration:mm\\:ss})";
            var shortest = validDurations.OrderBy(t => t.Duration).First();
            _shortestTrack = $"{shortest.Title} ({shortest.Duration:mm\\:ss})";
            var avgTicks = (long)validDurations.Average(t => t.Duration.Ticks);
            _averageTrackLength = TimeSpan.FromTicks(avgTicks).ToString(@"m\:ss");
        }
        else
        {
            _longestTrack = "-"; _shortestTrack = "-"; _averageTrackLength = "0:00";
        }

        TopArtists.Clear();
        foreach (var g in allTracks.Where(t => t.Artist != "Unknown" && !string.IsNullOrEmpty(t.Artist))
            .GroupBy(t => t.Artist).OrderByDescending(g => g.Sum(t => t.PlayCount)).Take(3))
            TopArtists.Add(new ArtistStat(g.Key, g.Sum(t => t.PlayCount)));

        var days = allTracks.Where(t => t.LastPlayed.HasValue).Select(t => t.LastPlayed!.Value.Date).Distinct().OrderBy(d => d).ToList();
        if (days.Count > 0) {
            int longest = 1, run = 1;
            for (int i = 1; i < days.Count; i++) { run = days[i] == days[i - 1].AddDays(1) ? run + 1 : 1; longest = Math.Max(longest, run); }
            _longestStreak = longest;
            int current = 0; var cursor = DateTime.Today;
            while (days.Contains(cursor)) { current++; cursor = cursor.AddDays(-1); }
            _currentStreak = current;
        } else { _currentStreak = 0; _longestStreak = 0; }

        _tasteDistribution = TagTaxonomy.ComputeDistribution(allTracks, TagTaxonomy.GenreAxes);
        _moodDistribution = TagTaxonomy.ComputeDistribution(allTracks, TagTaxonomy.MoodAxes);

        RecentlyPlayed.Clear();
        foreach (var t in allTracks.Where(t => t.LastPlayed.HasValue)
                                   .OrderByDescending(t => t.LastPlayed)
                                   .Take(5))
            RecentlyPlayed.Add(t);

        TopTags.Clear();
        foreach (var g in allTracks
                     .Where(t => t.Tags != null && t.Tags.Count > 0)
                     .SelectMany(t => t.Tags!)
                     .Where(t => !string.IsNullOrWhiteSpace(t))
                     .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(g => g.Count())
                     .Take(12))
            TopTags.Add(new TrackTag(g.Key, g.Count()));

        RefreshStatProperties();
        RefreshBadges();
        
        _shareQr = null;
    }

    private static string Loc(string key, string fallback)
    {
        var value = L(key);
        return string.IsNullOrEmpty(value) || value == key ? fallback : value;
    }

    private static IBrush Brush(string key)
        => Application.Current?.Resources.TryGetValue(key, out var value) == true && value is IBrush brush
            ? brush
            : new SolidColorBrush(Avalonia.Media.Colors.Gray);

    private static (MaterialIconKind Icon, IBrush Brush) ResolveImportedBadgeVisual(string type)
        => type.ToLowerInvariant() switch
        {
            "founder" => (MaterialIconKind.Crown, Brush("BrushAmber")),
            "dev" => (MaterialIconKind.CodeTags, Brush("BrushAccent")),
            "beta" => (MaterialIconKind.Flask, Brush("BrushAccent2")),
            _ => (MaterialIconKind.Certificate, Brush("BrushTextSecondary"))
        };

    private void RefreshBadges()
    {
        Badges.Clear();

        var imported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var badge in _grantedBadges.Where(b => b.Payload != null && b.VerifyAgainstMasterKey() && b.IsOfficial()))
        {
            var type = badge.Payload.BadgeType;
            if (string.IsNullOrWhiteSpace(type) || !imported.Add(type)) continue;

            var (icon, brush) = ResolveImportedBadgeVisual(type);
            var pretty = char.ToUpper(type[0]) + type[1..];
            var nameKey = $"Profile_Badge_{pretty}";
            Badges.Add(new ProfileBadge(
                type,
                icon,
                Loc(nameKey, pretty),
                Loc(nameKey + "_Desc", badge.Payload.IssuerName ?? "NullWave"),
                brush));
        }

        // NOTE: Removed the hardcoded "Alex/ZenQuant" developer badge. 
        // Developer status should be granted via a signed SignedBadge payload, not hardcoded username checks.

        if (!imported.Contains("early") && _createdAt != default && _createdAt < new DateTime(2025, 1, 1))
            Badges.Add(new ProfileBadge("early", MaterialIconKind.RocketLaunch,
                Loc("Profile_Badge_EarlyAdopter", "Early Adopter"),
                Loc("Profile_Badge_EarlyAdopter_Desc", "Joined before 2025."),
                Brush("BrushBlue")));

        if (!imported.Contains("collector") && TotalTracks >= 100)
            Badges.Add(new ProfileBadge("collector", MaterialIconKind.LibraryMusic,
                Loc("Profile_Badge_Collector", "Collector"),
                Loc("Profile_Badge_Collector_Desc", "100+ tracks in library."),
                Brush("BrushAccent")));

        if (!imported.Contains("heavy") && TotalPlays >= 1000)
            Badges.Add(new ProfileBadge("heavy", MaterialIconKind.Headphones,
                Loc("Profile_Badge_HeavyListener", "Heavy Listener"),
                Loc("Profile_Badge_HeavyListener_Desc", "1000+ plays."),
                Brush("BrushAccent2")));

        if (!imported.Contains("taste") && TotalFavorites >= 50)
            Badges.Add(new ProfileBadge("taste", MaterialIconKind.Star,
                Loc("Profile_Badge_TasteMaker", "Taste Maker"),
                Loc("Profile_Badge_TasteMaker_Desc", "50+ favorites."),
                Brush("BrushAmber")));

        if (!imported.Contains("local") && TotalTracks > 0 && LocalPercentage >= 80)
            Badges.Add(new ProfileBadge("local", MaterialIconKind.Harddisk,
                Loc("Profile_Badge_LocalPurist", "Local Purist"),
                Loc("Profile_Badge_LocalPurist_Desc", "80%+ local library."),
                Brush("BrushGreen")));

        if (!imported.Contains("wave") && SoundCloudCount > 0
            && SoundCloudCount >= YouTubeCount && SoundCloudCount >= LocalCount)
            Badges.Add(new ProfileBadge("wave", MaterialIconKind.Wave,
                Loc("Profile_Badge_WaveRider", "Wave Rider"),
                Loc("Profile_Badge_WaveRider_Desc", "SoundCloud-dominant library."),
                Brush("BrushSourceSoundCloud")));

        if (!imported.Contains("veteran") && _createdAt != default
            && (DateTime.Now - _createdAt).TotalDays >= 365)
            Badges.Add(new ProfileBadge("veteran", MaterialIconKind.Medal,
                Loc("Profile_Badge_Veteran", "Veteran"),
                Loc("Profile_Badge_Veteran_Desc", "1+ year with NullWave."),
                Brush("BrushAmber")));

        if (!imported.Contains("streak") && LongestStreak >= 7)
            Badges.Add(new ProfileBadge("streak", MaterialIconKind.Fire,
                Loc("Profile_Badge_Streak", "On Fire"),
                Loc("Profile_Badge_Streak_Desc", "7+ day listening streak."),
                Brush("BrushRed")));

        OnPropertyChanged(nameof(HasBadges));
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
        OnPropertyChanged(nameof(HasTopTrack));
        OnPropertyChanged(nameof(YouTubeCount));
        OnPropertyChanged(nameof(SoundCloudCount));
        OnPropertyChanged(nameof(LocalCount));
        OnPropertyChanged(nameof(YouTubePercentage));
        OnPropertyChanged(nameof(SoundCloudPercentage));
        OnPropertyChanged(nameof(LocalPercentage));
        OnPropertyChanged(nameof(YouTubePercentageText));
        OnPropertyChanged(nameof(SoundCloudPercentageText));
        OnPropertyChanged(nameof(LocalPercentageText));
        OnPropertyChanged(nameof(TotalListeningTimeDisplay));
        OnPropertyChanged(nameof(HasRecentlyPlayed));
        OnPropertyChanged(nameof(HasTopTags));
        OnPropertyChanged(nameof(HasTopArtists));
        OnPropertyChanged(nameof(LongestTrack));
        OnPropertyChanged(nameof(ShortestTrack));
        OnPropertyChanged(nameof(AverageTrackLength));
        OnPropertyChanged(nameof(CurrentStreak));
        OnPropertyChanged(nameof(LongestStreak));
        OnPropertyChanged(nameof(TasteDistribution));
        OnPropertyChanged(nameof(MoodDistribution));
    }

    private void MarkDirty() => OnPropertyChanged(nameof(IsDirty));

    private void DebouncedSave()
    {
        Dispatcher.UIThread.Post(() => IsSavingChanges = true);
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
            // FIX: Use NullWavePaths.DataDir instead of hardcoded Environment.SpecialFolder
            string dir = NullWavePaths.DataDir;
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

                    var rawAvatar = data.AvatarPath;
                    if (!string.IsNullOrEmpty(rawAvatar))
                    {
                        _avatarPath = Path.IsPathRooted(rawAvatar) && File.Exists(rawAvatar)
                            ? rawAvatar
                            : Path.Combine(GetProfileAssetsDirectory(), Path.GetFileName(rawAvatar));
                    }

                    var rawBanner = data.BannerImagePath;
                    if (!string.IsNullOrEmpty(rawBanner))
                    {
                        _bannerImagePath = Path.IsPathRooted(rawBanner) && File.Exists(rawBanner)
                            ? rawBanner
                            : Path.Combine(GetProfileAssetsDirectory(), Path.GetFileName(rawBanner));
                    }

                    _installId = data.InstallId ?? string.Empty;
                }
            }

            try
            {
                if (File.Exists(_badgesPath))
                    _grantedBadges = JsonSerializer.Deserialize<List<SignedBadge>>(File.ReadAllText(_badgesPath)) ?? new();
            }
            catch (Exception ex) { Log.Warning(ex, "Failed to load granted badges."); }

            if (string.IsNullOrEmpty(_installId))
            {
                _installId = _identity.Fingerprint;
                _ = SaveInternalAsync(showToast: false);
            }

            if (!string.IsNullOrEmpty(_avatarPath) && File.Exists(_avatarPath))
            {
                using var fileStream = File.OpenRead(_avatarPath);
                var memoryStream = new MemoryStream();
                fileStream.CopyTo(memoryStream);
                memoryStream.Position = 0;
                Avatar = new Bitmap(memoryStream);
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
        IsSavingChanges = true;
        await SaveInternalAsync(showToast: true);
    }

    private async Task SaveInternalAsync(bool showToast)
    {
        try
        {
            // FIX: Use NullWavePaths.DataDir
            string dir = NullWavePaths.DataDir;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string filePath = Path.Combine(dir, "profile.json");
            var data = new ProfileData(
                Username,
                Bio,
                string.IsNullOrEmpty(_avatarPath) ? null : Path.GetFileName(_avatarPath),
                string.IsNullOrEmpty(_bannerImagePath) ? null : Path.GetFileName(_bannerImagePath),
                _installId,
                _createdAt);

            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);

            _savedUsername = Username;
            _savedBio = Bio;
            _savedHasAvatar = HasAvatar;
            _savedAvatarPath = _avatarPath;
            _savedBannerImagePath = _bannerImagePath;
            MarkDirty();

            if (showToast)
            {
                await Dispatcher.UIThread.InvokeAsync(() => ShowSaveToast = true);
                await Task.Delay(2500);
                await Dispatcher.UIThread.InvokeAsync(() => ShowSaveToast = false);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save user profile.");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsSavingChanges = false);
        }
    }

    private async Task PickAvatarAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return;
        var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Avatar Image",
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll },
            AllowMultiple = false
        });

        var file = files.FirstOrDefault();
        if (file == null) return;

        string? oldAvatarPath = _avatarPath;
        try
        {
            string dir = GetProfileAssetsDirectory();
            Directory.CreateDirectory(dir);

            string extension = Path.GetExtension(file.Name);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".png";
            string targetPath = Path.Combine(dir, $"avatar_{DateTime.Now:yyyyMMdd_HHmmss_fff}{extension}");

            await using (var src = await file.OpenReadAsync())
            await using (var dst = File.Create(targetPath))
                await src.CopyToAsync(dst);

            await using var fileStream = File.OpenRead(targetPath);
            var memoryStream = new MemoryStream();
            await fileStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;
            Avatar = new Bitmap(memoryStream);
            _avatarPath = targetPath;

            await SaveInternalAsync(showToast: false);
            TryDeleteProfileAsset(oldAvatarPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to process selected avatar image.");
        }
    }

    private async void ResetAvatar()
    {
        string? oldAvatarPath = _avatarPath;
        Avatar = null;
        _avatarPath = null;
        await SaveInternalAsync(showToast: false);
        TryDeleteProfileAsset(oldAvatarPath);
    }

    // FIX: Route through NullWavePaths.DataDir
    private static string GetProfileAssetsDirectory()
        => Path.Combine(NullWavePaths.DataDir, "profile-assets");

    private static void TryDeleteProfileAsset(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            string fullPath = PathHelper.Resolve(path) ?? path;
            string assetsDirectory = Path.GetFullPath(GetProfileAssetsDirectory())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(fullPath);

            if (File.Exists(candidate)
                && candidate.StartsWith(assetsDirectory, StringComparison.OrdinalIgnoreCase))
                File.Delete(candidate);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to delete old profile asset: {Path}", path);
        }
    }

    public void TriggerExportSuccessToast() => _ = ShowToastAsync("Profile Card Exported!");

    private async Task ShowToastAsync(string message)
    {
        ToastMessage = message;
        ShowSaveToast = true;
        await Task.Delay(2500);
        ShowSaveToast = false;
    }

    private string _shareCode = "";
    public string ShareCode => _shareCode;

    private Bitmap? _shareQr;
    public Bitmap? ShareQrBitmap => _shareQr;

    public string ShareString => ProfileShareService.Encode(BuildSharePayload());

    public ProfileSharePayload? LastGuest { get; private set; }
    public double GuestOverlap { get; private set; }
    public string GuestOverlapText => LastGuest == null ? "" : $"You share {(int)(GuestOverlap * 100)}% taste with {LastGuest.Name}";

    public void EnsureShareAssets()
    {
        if (string.IsNullOrEmpty(_shareCode))
        {
            _shareCode = ProfileShareService.GetPublicCode(_identity);
            OnPropertyChanged(nameof(ShareCode));
        }
        if (_shareQr == null)
        {
            _shareQr = ProfileShareService.GenerateQrBitmap(ShareString);
            OnPropertyChanged(nameof(ShareQrBitmap));
        }
        OnPropertyChanged(nameof(ShareString));
    }

    public ProfileSharePayload BuildSharePayload() => new()
    {
        Code = ShareCode,
        Name = Username,
        Bio = Bio.Length > 80 ? Bio[..80] : Bio,
        TopArtists = TopArtists.Take(3).Select(a => a.Name).ToList(),
        TopTags = TopTags.Take(5).Select(t => t.Tag).ToList(),
        TopTracks = _library?.GetAll()
            .Where(t => t.PlayCount > 0)
            .OrderByDescending(t => t.PlayCount)
            .Take(5)
            .Select(t => new ProfileSharePayload.SharedTrack(t.Title, t.Artist))
            .ToList() ?? new(),
        Badges = Badges.Select(b => b.Id).ToList(),
        Tracks = TotalTracks,
        Favorites = TotalFavorites,
        Hours = (int)_totalListeningTime.TotalHours,
    };

    [RelayCommand]
    private async Task CopyShareStringAsync()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d && d.MainWindow?.Clipboard is { } cb)
        {
            await cb.SetTextAsync(ShareString);
            ToastService.Instance.Show("Share string copied to clipboard!", ToastType.Success);
        }
    }

    [RelayCommand]
    public void ImportShareString(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return;
        
        string payloadStr = input;
        if (input.StartsWith("nullwave://p/", StringComparison.OrdinalIgnoreCase))
            payloadStr = input.Substring("nullwave://p/".Length);

        var payload = ProfileShareService.Decode(payloadStr);
        if (payload == null) 
        { 
            ToastService.Instance.Show("Invalid NullWave share code.", ToastType.Error); 
            return; 
        }
        
        LastGuest = payload;
        GuestOverlap = ProfileShareService.ComputeTasteOverlap(payload, _library?.GetAll() ?? new List<Track>());
        OnPropertyChanged(nameof(LastGuest));
        OnPropertyChanged(nameof(GuestOverlapText));
        ToastService.Instance.Show($"Loaded {payload.Name}'s profile!", ToastType.Success);
    }

    [RelayCommand]
    private void QueueMatchingTracks()
    {
        if (LastGuest == null || _library == null) return;
        
        int queued = 0;
        foreach (var shared in LastGuest.TopTracks)
        {
            var match = _library.GetAll().FirstOrDefault(t => 
                t.Title.Equals(shared.Title, StringComparison.OrdinalIgnoreCase) && 
                t.Artist.Equals(shared.Artist, StringComparison.OrdinalIgnoreCase));
                
            if (match != null)
            {
                _library.AddToQueue(match.Id);
                queued++;
            }
        }
        ToastService.Instance.Show(queued > 0 ? $"Queued {queued} matching track(s)!" : "No matching tracks found in your library.", queued > 0 ? ToastType.Success : ToastType.Info);
    }

    public void Dispose()
    {
        _saveTimer?.Dispose();
        LocalizationService.Instance.PropertyChanged -= _langHandler;
        if (_library != null)
            _library.LibraryChanged -= OnLibraryChanged;
    }
}