using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using NullWave.ViewModels;

namespace NullWave.Views.Controls;

public partial class TrackListView : DockPanel
{
    public TrackListView()
    {
        InitializeComponent();
    }

    private void OnTrackSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.Library.SelectedTrack != null)
        {
            vm.Detail.OpenFor(vm.Library.SelectedTrack);
        }
    }

    private void OnTrackDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.Library.SelectedTrack != null)
        {
            vm.Library.PlayTrackCommand.Execute(vm.Library.SelectedTrack);
        }
    }

    // TODO: Clipboard pre-fill for Add Track flyout — deferred. Avalonia's IClipboard
    // surface on this version doesn't expose GetTextAsync() or GetDataAsync(DataFormats)
    // as expected (both failed to compile — DataFormats is obsolete in favor of
    // DataFormat, and the exact replacement method/signature wasn't confirmed). Revisit
    // by checking clipboard.<autocomplete> in the IDE to find the real method name
    // before re-wiring this.
    //
    // private async void OnAddFlyoutOpened(object? sender, EventArgs e)
    // {
    //     if (DataContext is not MainViewModel vm) return;
    //
    //     if (!string.IsNullOrWhiteSpace(vm.Input.InputUrl)) return;
    //
    //     try
    //     {
    //         var topLevel = TopLevel.GetTopLevel(this);
    //         var clipboard = topLevel?.Clipboard;
    //         if (clipboard == null) return;
    //
    //         var textObj = await clipboard.GetDataAsync(Avalonia.Input.DataFormats.Text);
    //         var text = textObj as string;
    //         if (string.IsNullOrWhiteSpace(text)) return;
    //
    //         text = text.Trim();
    //
    //         vm.Input.InputUrl = text;
    //         if (!vm.Input.IsInputUrlValid)
    //         {
    //             vm.Input.InputUrl = string.Empty;
    //         }
    //     }
    //     catch
    //     {
    //         // Clipboard access can fail — silently skip pre-fill.
    //     }
    // }
}