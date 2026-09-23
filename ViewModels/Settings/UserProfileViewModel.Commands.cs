using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Serilog;
using NullWave.Models;
using NullWave.Services;
using NullWave.Helpers;

namespace NullWave.ViewModels;

public partial class UserProfileViewModel
{
    private void InitializeCommands()
    {
        PickAvatarCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(async () => await PickAvatarAsync());
        ResetAvatarCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(ResetAvatar);
        ManualSaveCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(async () => await ManualSaveAsync());
        ToggleEditorCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => IsEditorOpen = !IsEditorOpen);
        OpenEditorCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => IsEditorOpen = true);
        CloseEditorCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => IsEditorOpen = false);
        
        // FIX: Added ?? "accent" to prevent CS8601 null reference warning
        SetBannerColorCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<string>(color => BannerColor = color ?? "accent");
        
        PickBannerImageCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(async () => await PickBannerImageAsync());
        ClearBannerImageCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(async () => await ClearBannerImageAsync());
        
        // FIX: Added ?? "default" to prevent CS8601 null reference warning
        SetProfileFrameCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<string>(style => ProfileFrameStyle = style ?? "default");
        
        FilterByTagCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<string?>(tag => { if (!string.IsNullOrEmpty(tag)) TagClickedRequested?.Invoke(tag); });
        PlayTopTrackCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { if (_mostPlayedTrackId is { } id) PlayTrackByIdRequested?.Invoke(id); });
        CopyInstallIdCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(async () => await CopyInstallIdAsync());
        CopyShareStringCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(async () => await CopyShareStringAsync());
        
        // FIX: Wrapped in lambda to ensure nullable signature matches perfectly without CS8601
        ImportShareStringCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<string?>(input => ImportShareString(input));
        
        QueueMatchingTracksCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(QueueMatchingTracks);
    }

    private async Task CopyInstallIdAsync()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(InstallId);
                ToastService.Instance.Show(LocalizationService.Instance["Profile_Account_CopyId_Done"], ToastType.Success);
            }
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to copy Install ID to clipboard."); }
    }

    private async Task PickBannerImageAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return;
        var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
        if (topLevel == null) return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Select Banner Image", FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }, AllowMultiple = false });
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
            await using (var dst = File.Create(targetPath)) await src.CopyToAsync(dst);
            BannerImagePath = targetPath;
            await SaveInternalAsync(showToast: false);
            TryDeleteProfileAsset(oldBannerPath);
        }
        catch (Exception ex) { Log.Error(ex, "Failed to set banner image."); ToastService.Instance.Show(LocalizationService.Instance["Profile_Badge_Invalid"], ToastType.Error); }
    }

    private async Task ClearBannerImageAsync()
    {
        string? oldBannerPath = BannerImagePath;
        BannerImagePath = null;
        await SaveInternalAsync(showToast: false);
        TryDeleteProfileAsset(oldBannerPath);
    }

    private async Task PickAvatarAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return;
        var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
        if (topLevel == null) return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Select Avatar Image", FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }, AllowMultiple = false });
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
            await using (var dst = File.Create(targetPath)) await src.CopyToAsync(dst);
            await using var fileStream = File.OpenRead(targetPath);
            var memoryStream = new MemoryStream();
            await fileStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;
            Avatar = new Bitmap(memoryStream);
            _avatarPath = targetPath;
            await SaveInternalAsync(showToast: false);
            TryDeleteProfileAsset(oldAvatarPath);
        }
        catch (Exception ex) { Log.Error(ex, "Failed to process selected avatar image."); }
    }

    private async void ResetAvatar()
    {
        string? oldAvatarPath = _avatarPath;
        Avatar = null;
        _avatarPath = null;
        await SaveInternalAsync(showToast: false);
        TryDeleteProfileAsset(oldAvatarPath);
    }

    public void EnsureShareAssets()
    {
        if (string.IsNullOrEmpty(_shareCode)) { _shareCode = ProfileShareService.GetPublicCode(_identity); OnPropertyChanged(nameof(ShareCode)); }
        if (_shareQr == null) { _shareQr = ProfileShareService.GenerateQrBitmap(ShareString); OnPropertyChanged(nameof(ShareQrBitmap)); }
        OnPropertyChanged(nameof(ShareString));
    }

    public ProfileSharePayload BuildSharePayload() => new()
    {
        Code = ShareCode,
        Name = Username,
        Bio = Bio.Length > 80 ? Bio[..80] : Bio,
        TopArtists = TopArtists.Take(3).Select(a => a.Name).ToList(),
        TopTags = TopTags.Take(5).Select(t => t.Tag).ToList(),
        TopTracks = _library?.GetAll().Where(t => t.PlayCount > 0).OrderByDescending(t => t.PlayCount).Take(5).Select(t => new ProfileSharePayload.SharedTrack(t.Title, t.Artist)).ToList() ?? new(),
        Badges = Badges.Select(b => b.Id).ToList(),
        Tracks = TotalTracks,
        Favorites = TotalFavorites,
        Hours = (int)_totalListeningTime.TotalHours,
    };

    private async Task CopyShareStringAsync()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d && d.MainWindow?.Clipboard is { } cb)
        {
            await cb.SetTextAsync(ShareString);
            ToastService.Instance.Show("Share string copied to clipboard!", ToastType.Success);
        }
    }

    // FIX: Changed from 'private' to 'public' so ProfileWindow.axaml.cs can access it directly
    public void ImportShareString(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return;
        string payloadStr = input.StartsWith("nullwave://p/", StringComparison.OrdinalIgnoreCase) ? input.Substring("nullwave://p/".Length) : input;
        var payload = ProfileShareService.Decode(payloadStr);
        if (payload == null) { ToastService.Instance.Show("Invalid NullWave share code.", ToastType.Error); return; }
        LastGuest = payload;
        GuestOverlap = ProfileShareService.ComputeTasteOverlap(payload, _library?.GetAll() ?? new List<Track>());
        OnPropertyChanged(nameof(LastGuest));
        OnPropertyChanged(nameof(GuestOverlapText));
        ToastService.Instance.Show($"Loaded {payload.Name}'s profile!", ToastType.Success);
    }

    private void QueueMatchingTracks()
    {
        if (LastGuest == null || _library == null) return;
        int queued = 0;
        foreach (var shared in LastGuest.TopTracks)
        {
            var match = _library.GetAll().FirstOrDefault(t => t.Title.Equals(shared.Title, StringComparison.OrdinalIgnoreCase) && t.Artist.Equals(shared.Artist, StringComparison.OrdinalIgnoreCase));
            if (match != null) { _library.AddToQueue(match.Id); queued++; }
        }
        ToastService.Instance.Show(queued > 0 ? $"Queued {queued} matching track(s)!" : "No matching tracks found in your library.", queued > 0 ? ToastType.Success : ToastType.Info);
    }

    public void TriggerExportSuccessToast() => _ = ShowToastAsync("Profile Card Exported!");

    private async Task ShowToastAsync(string message)
    {
        ToastMessage = message;
        ShowSaveToast = true;
        await Task.Delay(2500);
        ShowSaveToast = false;
    }
}