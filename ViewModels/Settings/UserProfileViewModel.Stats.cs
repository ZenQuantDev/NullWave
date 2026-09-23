using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Material.Icons;
using Serilog;
using NullWave.Models;
using NullWave.Services;
using NullWave.Services.Security;
using NullWave.Helpers;

namespace NullWave.ViewModels;

public partial class UserProfileViewModel
{
    private void OnLibraryChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(UpdateStatistics);

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
            RecentlyPlayed.Clear(); TopTags.Clear(); TopArtists.Clear();
            RefreshStatProperties(); RefreshBadges(); return;
        }

        _totalTracks = allTracks.Count;
        _totalFavorites = allTracks.Count(t => t.IsFavorite);
        _totalPlays = allTracks.Sum(t => t.PlayCount);
        _totalSkips = allTracks.Sum(t => t.SkipCount);

        var topTrack = allTracks.OrderByDescending(t => t.PlayCount).FirstOrDefault(t => t.PlayCount > 0);
        _mostPlayedTrack = topTrack?.Title ?? "-";
        _mostPlayedTrackId = topTrack?.Id;
        _mostPlayedTrackArtPath = topTrack?.AlbumArtPath;

        _mostPlayedArtist = allTracks.Where(t => t.Artist != "Unknown" && !string.IsNullOrEmpty(t.Artist)).GroupBy(t => t.Artist).OrderByDescending(g => g.Sum(t => t.PlayCount)).FirstOrDefault()?.Key ?? "-";
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
        else { _longestTrack = "-"; _shortestTrack = "-"; _averageTrackLength = "0:00"; }

        TopArtists.Clear();
        foreach (var g in allTracks.Where(t => t.Artist != "Unknown" && !string.IsNullOrEmpty(t.Artist)).GroupBy(t => t.Artist).OrderByDescending(g => g.Sum(t => t.PlayCount)).Take(3))
            TopArtists.Add(new ArtistStat(g.Key, g.Sum(t => t.PlayCount)));

        var days = allTracks.Where(t => t.LastPlayed.HasValue).Select(t => t.LastPlayed!.Value.Date).Distinct().OrderBy(d => d).ToList();
        if (days.Count > 0)
        {
            int longest = 1, run = 1;
            for (int i = 1; i < days.Count; i++) { run = days[i] == days[i - 1].AddDays(1) ? run + 1 : 1; longest = Math.Max(longest, run); }
            _longestStreak = longest;
            int current = 0; var cursor = DateTime.Today;
            while (days.Contains(cursor)) { current++; cursor = cursor.AddDays(-1); }
            _currentStreak = current;
        }
        else { _currentStreak = 0; _longestStreak = 0; }

        _tasteDistribution = TagTaxonomy.ComputeDistribution(allTracks, TagTaxonomy.GenreAxes);
        _moodDistribution = TagTaxonomy.ComputeDistribution(allTracks, TagTaxonomy.MoodAxes);

        RecentlyPlayed.Clear();
        foreach (var t in allTracks.Where(t => t.LastPlayed.HasValue).OrderByDescending(t => t.LastPlayed).Take(5)) RecentlyPlayed.Add(t);

        TopTags.Clear();
        foreach (var g in allTracks.Where(t => t.Tags != null && t.Tags.Count > 0).SelectMany(t => t.Tags!).Where(t => !string.IsNullOrWhiteSpace(t)).GroupBy(t => t, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Take(12))
            TopTags.Add(new TrackTag(g.Key, g.Count()));

        RefreshStatProperties();
        RefreshBadges();
        _shareQr = null;
    }

    private void RefreshStatProperties()
    {
        OnPropertyChanged(nameof(TotalTracks)); OnPropertyChanged(nameof(TotalFavorites)); OnPropertyChanged(nameof(TotalPlays)); OnPropertyChanged(nameof(TotalSkips));
        OnPropertyChanged(nameof(MostPlayedTrack)); OnPropertyChanged(nameof(MostPlayedArtist)); OnPropertyChanged(nameof(HasMostPlayedTrackArt)); OnPropertyChanged(nameof(MostPlayedTrackArtPath));
        OnPropertyChanged(nameof(HasTopTrack)); OnPropertyChanged(nameof(YouTubeCount)); OnPropertyChanged(nameof(SoundCloudCount)); OnPropertyChanged(nameof(LocalCount));
        OnPropertyChanged(nameof(YouTubePercentage)); OnPropertyChanged(nameof(SoundCloudPercentage)); OnPropertyChanged(nameof(LocalPercentage));
        OnPropertyChanged(nameof(YouTubePercentageText)); OnPropertyChanged(nameof(SoundCloudPercentageText)); OnPropertyChanged(nameof(LocalPercentageText));
        OnPropertyChanged(nameof(TotalListeningTimeDisplay)); OnPropertyChanged(nameof(HasRecentlyPlayed)); OnPropertyChanged(nameof(HasTopTags)); OnPropertyChanged(nameof(HasTopArtists));
        OnPropertyChanged(nameof(LongestTrack)); OnPropertyChanged(nameof(ShortestTrack)); OnPropertyChanged(nameof(AverageTrackLength));
        OnPropertyChanged(nameof(CurrentStreak)); OnPropertyChanged(nameof(LongestStreak)); OnPropertyChanged(nameof(TasteDistribution)); OnPropertyChanged(nameof(MoodDistribution));
    }

    private static string Loc(string key, string fallback) { var value = LocalizationService.Instance[key]; return string.IsNullOrEmpty(value) || value == key ? fallback : value; }
    private static IBrush Brush(string key) => Application.Current?.Resources.TryGetValue(key, out var value) == true && value is IBrush brush ? brush : new SolidColorBrush(Avalonia.Media.Colors.Gray);
    private static (MaterialIconKind Icon, IBrush Brush) ResolveImportedBadgeVisual(string type) => type.ToLowerInvariant() switch
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
            Badges.Add(new ProfileBadge(type, icon, Loc(nameKey, pretty), Loc(nameKey + "_Desc", badge.Payload.IssuerName ?? "NullWave"), brush));
        }

        if (!imported.Contains("early") && _createdAt != default && _createdAt < new DateTime(2025, 1, 1))
            Badges.Add(new ProfileBadge("early", MaterialIconKind.RocketLaunch, Loc("Profile_Badge_EarlyAdopter", "Early Adopter"), Loc("Profile_Badge_EarlyAdopter_Desc", "Joined before 2025."), Brush("BrushBlue")));
        if (!imported.Contains("collector") && TotalTracks >= 100)
            Badges.Add(new ProfileBadge("collector", MaterialIconKind.LibraryMusic, Loc("Profile_Badge_Collector", "Collector"), Loc("Profile_Badge_Collector_Desc", "100+ tracks in library."), Brush("BrushAccent")));
        if (!imported.Contains("heavy") && TotalPlays >= 1000)
            Badges.Add(new ProfileBadge("heavy", MaterialIconKind.Headphones, Loc("Profile_Badge_HeavyListener", "Heavy Listener"), Loc("Profile_Badge_HeavyListener_Desc", "1000+ plays."), Brush("BrushAccent2")));
        if (!imported.Contains("taste") && TotalFavorites >= 50)
            Badges.Add(new ProfileBadge("taste", MaterialIconKind.Star, Loc("Profile_Badge_TasteMaker", "Taste Maker"), Loc("Profile_Badge_TasteMaker_Desc", "50+ favorites."), Brush("BrushAmber")));
        if (!imported.Contains("local") && TotalTracks > 0 && LocalPercentage >= 80)
            Badges.Add(new ProfileBadge("local", MaterialIconKind.Harddisk, Loc("Profile_Badge_LocalPurist", "Local Purist"), Loc("Profile_Badge_LocalPurist_Desc", "80%+ local library."), Brush("BrushGreen")));
        if (!imported.Contains("wave") && SoundCloudCount > 0 && SoundCloudCount >= YouTubeCount && SoundCloudCount >= LocalCount)
            Badges.Add(new ProfileBadge("wave", MaterialIconKind.Wave, Loc("Profile_Badge_WaveRider", "Wave Rider"), Loc("Profile_Badge_WaveRider_Desc", "SoundCloud-dominant library."), Brush("BrushSourceSoundCloud")));
        if (!imported.Contains("veteran") && _createdAt != default && (DateTime.Now - _createdAt).TotalDays >= 365)
            Badges.Add(new ProfileBadge("veteran", MaterialIconKind.Medal, Loc("Profile_Badge_Veteran", "Veteran"), Loc("Profile_Badge_Veteran_Desc", "1+ year with NullWave."), Brush("BrushAmber")));
        if (!imported.Contains("streak") && LongestStreak >= 7)
            Badges.Add(new ProfileBadge("streak", MaterialIconKind.Fire, Loc("Profile_Badge_Streak", "On Fire"), Loc("Profile_Badge_Streak_Desc", "7+ day listening streak."), Brush("BrushRed")));

        OnPropertyChanged(nameof(HasBadges));
    }

    public void ImportBadge(string json)
    {
        try
        {
            var badge = JsonSerializer.Deserialize<SignedBadge>(json);
            if (badge == null || !badge.VerifyAgainstMasterKey() || !badge.IsOfficial()) { ToastService.Instance.Show(LocalizationService.Instance["Profile_Badge_Invalid"], ToastType.Error); return; }
            if (!badge.IsBoundTo(_identity.Fingerprint)) { ToastService.Instance.Show(LocalizationService.Instance["Profile_Badge_NotYours"], ToastType.Warning); return; }
            if (_grantedBadges.Any(b => b.Payload.BadgeType == badge.Payload.BadgeType)) { ToastService.Instance.Show(LocalizationService.Instance["Profile_Badge_AlreadyOwned"], ToastType.Info); return; }
            
            Directory.CreateDirectory(Path.GetDirectoryName(_badgesPath)!);
            _grantedBadges.Add(badge);
            File.WriteAllText(_badgesPath, JsonSerializer.Serialize(_grantedBadges));
            Dispatcher.UIThread.Post(RefreshBadges);
            ToastService.Instance.Show(LocalizationService.Instance["Profile_Badge_Unlocked"], ToastType.Success);
        }
        catch (Exception ex) { Log.Error(ex, "Badge import failed"); ToastService.Instance.Show(LocalizationService.Instance["Profile_Badge_Invalid"], ToastType.Error); }
    }
}