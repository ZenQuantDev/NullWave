using NullWave.Models;
using NullWave.Services;
using Xunit;

namespace NullWave.Tests;

[Collection("Database")]
public class ToastServiceTests
{
    [Fact]
    public void UpdateLiveActivity_with_title_updates_the_title()
    {
        var toast = ToastService.Instance.StartLiveActivity("Working", "step 1", scope: "unit-title-1");

        ToastService.Instance.UpdateLiveActivity(toast, "step 2", title: "Still working");

        Assert.Equal("Still working", toast.Title);
        Assert.Equal("step 2", toast.Message);
        ToastService.Instance.Dismiss(toast);
    }

    [Fact]
    public void StartLiveActivity_reuses_scoped_activity_and_updates_its_title()
    {
        var first  = ToastService.Instance.StartLiveActivity("Old title", "old", scope: "unit-title-2");
        var second = ToastService.Instance.StartLiveActivity("New title", "new", scope: "unit-title-2");

        Assert.Same(first, second);
        Assert.Equal("New title", second.Title);
        Assert.Equal("new", second.Message);
        ToastService.Instance.Dismiss(second);
    }

    [Fact]
    public void CompleteLiveActivity_sets_final_message_type_and_progress()
    {
        var toast = ToastService.Instance.StartLiveActivity("Working", "step 1", scope: "unit-title-3");

        ToastService.Instance.CompleteLiveActivity(toast, "All done", finalType: ToastType.Success);

        Assert.Equal("All done", toast.Message);
        Assert.Equal(ToastType.Success, toast.Type);
        Assert.True(toast.IsCompleted);
        Assert.False(toast.IsIndeterminate);
        Assert.Equal(100, toast.ProgressValue);
        ToastService.Instance.Dismiss(toast);
    }

    [Fact]
    public void Show_with_scope_reuses_existing_toast_and_updates_title()
    {
        var toast = ToastService.Instance.Show("first", ToastType.Info, scope: "unit-title-4");
        var again = ToastService.Instance.Show("second", ToastType.Warning, title: "Renamed", scope: "unit-title-4");

        Assert.Same(toast, again);
        Assert.Equal("Renamed", again.Title);
        Assert.Equal("second", again.Message);
        ToastService.Instance.Dismiss(again);
    }
}