using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using NullWave.Services.Radio;
using Serilog;

namespace NullWave.ViewModels;

public partial class MainViewModel
{
    private async Task PreviewRadioHealAsync()
    {
        var activity = ToastService.Instance.StartLiveActivity(
            "Radio check", "Checking stations…", isIndeterminate: true, scope: "radio-heal");
        try
        {
            if (!_radioHealer.IsProviderAvailable)
            {
                ToastService.Instance.CompleteLiveActivity(
                    activity, "Radio provider unavailable.", finalType: ToastType.Warning);
                return;
            }

            var preview = await _radioHealer.PreviewAsync();
            var healthy = preview.Count(item => item.Status == RadioHealStatus.Healthy);
            var moved = preview.Count(item => item.Status == RadioHealStatus.Moved);
            var dead = preview.Count(item => item.Status == RadioHealStatus.Dead);
            ToastService.Instance.CompleteLiveActivity(
                activity, $"{healthy} healthy · {moved} moved · {dead} gone");
            foreach (var item in preview.Where(item =>
                         item.Status is RadioHealStatus.Moved or RadioHealStatus.Dead))
            {
                Log.Information("[RadioHeal] {Status}: {Title}", item.Status, item.Track.Title);
            }
        }
        catch (Exception ex)
        {
            ToastService.Instance.CompleteLiveActivity(
                activity, "Radio check failed.", finalType: ToastType.Error);
            Log.Warning(ex, "[RadioHeal] preview failed");
        }
    }

    private async Task RepairRadioHealAsync()
    {
        try
        {
            if (!_radioHealer.IsProviderAvailable)
            {
                ToastService.Instance.Show(
                    "Radio provider unavailable.", ToastType.Warning, scope: "radio-heal");
                return;
            }

            var preview = await _radioHealer.PreviewAsync();
            var moved = preview.Where(item => item.Status == RadioHealStatus.Moved).ToList();
            var dead = preview.Where(item => item.Status == RadioHealStatus.Dead).ToList();

            if (moved.Count == 0 && dead.Count == 0)
            {
                ToastService.Instance.Show(
                    "All radio streams are up to date.", ToastType.Info, scope: "radio-heal");
                return;
            }

            var message = new StringBuilder();
            message.AppendLine($"{moved.Count} station(s) moved to a new URL:");
            foreach (var item in moved.Take(10)) message.AppendLine($"  • {item.Track.Title}");
            if (moved.Count > 10) message.AppendLine($"  …and {moved.Count - 10} more");
            message.AppendLine();
            message.AppendLine($"{dead.Count} station(s) no longer exist in the directory.");
            message.AppendLine("They will be tagged 'dead' and kept in your library.");
            foreach (var item in dead.Take(10)) message.AppendLine($"  • {item.Track.Title}");

            var confirmed = await DialogService.Instance.ShowDialogAsync<bool>(
                new Views.ConfirmDialog("Repair radio streams?", message.ToString()));
            if (!confirmed) return;

            var changed = await _radioHealer.ApplyAsync(preview);
            Dispatcher.UIThread.Post(() => { Library.Refresh(); RadioLibrary.Refresh(); });
            ToastService.Instance.Show(
                $"Repaired {changed} radio station(s).", ToastType.Success, scope: "radio-heal");
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show("Radio repair failed.", ToastType.Error, scope: "radio-heal");
            Log.Warning(ex, "[RadioHeal] repair failed");
        }
    }

    private async void OnRadioPlaybackFailed(Track track)
    {
        try
        {
            if (await _radioHealer.TryHealOnFailureAsync(track))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Player.RetryTrack(track);
                    ToastService.Instance.Show(
                        $"Stream moved — reconnected to '{track.Title}'.",
                        ToastType.Info,
                        scope: "radio-heal");
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[RadioHeal] auto-heal failed");
        }
    }
}
