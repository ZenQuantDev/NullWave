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
    private DispatcherTimer? _statsTimer;

    private void OnLibraryChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (_statsTimer == null)
        {
            _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _statsTimer.Tick += (_, _) => { _statsTimer.Stop(); UpdateStatistics(); };
        }
        _statsTimer.Stop();
        _statsTimer.Start();
    });


    public void UpdateStatistics()
    {
        if (_library == null) return;
        var everything = _library.GetAll().ToList();
        _radioStations = everything.Count(t => t.MediaType == MediaType.Radio);
        _audiobookHours = everything.Where(t => t.MediaType == MediaType.Audiobook).Sum(t => t.Duration.TotalHours);
        var allTracks = everything.Where(t => t.MediaType == MediaType.Music).ToList();
        if (allTracks.Count == 0)
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


        var streaks = ListeningStreaks.Compute(
            allTracks.Where(t => t.LastPlayed.HasValue).Select(t => t.LastPlayed!.Value),
            DateTime.UtcNow);

        _currentStreak = streaks.Current;
        _longestStreak = streaks.Longest;

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

    private double? BadgeMetric(string name) => name switch
    {
        "totalTracks" => TotalTracks,
        "totalPlays" => TotalPlays,
        "totalFavorites" => TotalFavorites,
        "localPercentage" => LocalPercentage,
        "longestStreak" => LongestStreak,
        "radioStations" => _radioStations,
        "audiobookHours" => _audiobookHours,
        "daysSinceCreation" => _createdAt == default ? 0 : (DateTime.UtcNow - UtcTime.AsUtc(_createdAt)).TotalDays,
        "soundCloudDominant" => SoundCloudCount > 0 && SoundCloudCount >= YouTubeCount && SoundCloudCount >= LocalCount ? 1 : 0,
        _ => null
    };

    private static ProfileBadge ToProfileBadge(BadgeDefinition d)
    {
        var icon = Enum.TryParse<MaterialIconKind>(d.Icon, out var k) ? k : MaterialIconKind.Certificate;
        return new ProfileBadge(d.Id, icon, Loc(d.NameKey, d.Id), Loc(d.DescKey, ""), Brush(d.Color));
    }

    private void RefreshBadges()
    {
        Badges.Clear();
        BadgeCatalog.Load();
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var badge in _grantedBadges)
        {
            if (badge.Payload == null || badge.IsExpired || !badge.IsOfficial()) continue;
            var type = badge.Payload.BadgeType;
            if (string.IsNullOrWhiteSpace(type) || !owned.Add(type)) continue;

            var def = BadgeCatalog.GetById(type);
            if (def != null && def.Kind == BadgeKind.Granted)
                Badges.Add(ToProfileBadge(def));
            else
                Badges.Add(new ProfileBadge(type, MaterialIconKind.Certificate, type,
                    badge.Payload.IssuerName ?? "NullWave", Brush("BrushTextSecondary")));
        }

        DateTime? createdUtc = _createdAt == default ? null : UtcTime.AsUtc(_createdAt);
        foreach (var def in BadgeCatalog.GetComputed())
            if (def.Rule != null && !owned.Contains(def.Id) && BadgeRuleEvaluator.IsEarned(def.Rule, BadgeMetric, createdUtc))
                Badges.Add(ToProfileBadge(def));

        OnPropertyChanged(nameof(HasBadges));
    }

    public void ImportBadge(string json)
    {
        try
        {
            if (json.Length > 16_384)
            {
                ToastService.Instance.Show(LocalizationService.Instance["Profile_Badge_Invalid"], ToastType.Error);
                return;
            }
            
            var badge = JsonSerializer.Deserialize<SignedBadge>(json);
            if (badge == null || badge.Payload == null || badge.IsExpired || !badge.IsOfficial())
            {
                ToastService.Instance.Show(LocalizationService.Instance["Profile_Badge_Invalid"], ToastType.Error);
                return;
            }
            
            if (!badge.IsBoundTo(_identity.Fingerprint))
            {
                ToastService.Instance.Show(LocalizationService.Instance["Profile_Badge_NotYours"], ToastType.Warning);
                return;
            }
            
            if (_grantedBadges.Any(b => string.Equals(b.Payload?.BadgeType, badge.Payload.BadgeType, StringComparison.OrdinalIgnoreCase)))
            {
                ToastService.Instance.Show(LocalizationService.Instance["Profile_Badge_AlreadyOwned"], ToastType.Info);
                return;
            }
            
            Directory.CreateDirectory(Path.GetDirectoryName(_badgesPath)!);
            var next = new List<SignedBadge>(_grantedBadges) { badge };
            var tmp = _badgesPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(next));
            File.Move(tmp, _badgesPath, overwrite: true);
            _grantedBadges = next;
            
            Dispatcher.UIThread.Post(RefreshBadges);
            ToastService.Instance.Show(LocalizationService.Instance["Profile_Badge_Unlocked"], ToastType.Success);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Badge import failed");
            ToastService.Instance.Show(LocalizationService.Instance["Profile_Badge_Invalid"], ToastType.Error);
        }
    }
}
