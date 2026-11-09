using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NullWave.Helpers;
using NullWave.Helpers.Diagnostics;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels;
using NullWave.Views.Settings;

namespace NullWave.Views;

public partial class SettingsWindow : Window
{
    private readonly Dictionary<string, Control> _pages = new();
    private SettingsViewModel? _vm;

    public SettingsWindow()
    {
        InitializeComponent();
        WallpaperChromeSync.Attach(this);

        Opened += (_, _) => ToastService.Instance.SetActiveHost(true);
        Closed += (_, _) => { ToastService.Instance.SetActiveHost(false); Detach(); };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F3) { PerfOverlayController.Toggle(); e.Handled = true; }
        };

        DataContextChanged += (_, _) =>
        {
            Detach();
            _vm = DataContext as SettingsViewModel;
            if (_vm == null) return;
            _vm.PropertyChanged += OnVmChanged;
            ShowPage(_vm.CurrentSettingsPage);
        };
    }

    private void Detach() 
    { 
        if (_vm != null) _vm.PropertyChanged -= OnVmChanged; 
        _vm = null; 
    }

    private void OnVmChanged(object? s, PropertyChangedEventArgs e)
    {
        if (_vm == null) return;
        if (e.PropertyName == nameof(SettingsViewModel.CurrentSettingsPage)) 
            ShowPage(_vm.CurrentSettingsPage);
        else if (e.PropertyName == nameof(SettingsViewModel.ShowWaveSpine)) 
            ScheduleBead();
    }

    private static Control Build(string k) => k switch
    {
        "General" => new GeneralTab(), 
        "Appearance" => new AppearanceTab(), 
        "Audio" => new AudioTab(),
        "Downloads" => new DownloadsTab(), 
        "Integrations" => new IntegrationsTab(), 
        "ApiKeys" => new ApiKeysTab(),
        "SmartFeatures" => new SmartSortingTab(), 
        "Maintenance" => new MaintenanceTab(), 
        "Updates" => new UpdatesTab(),
        "About" => new AboutTab(), 
        "Help" => new HelpTab(), 
        _ => new DevTab()
    };

    private void ShowPage(string key)
    {
        if (!_pages.TryGetValue(key, out var page))
        {
            page = Build(key);
            page.DataContext = DataContext;
            _pages[key] = page;
        }
        PageHost.PageTransition = EffectsTierResolver.Shared.Current == EffectsTier.Minimal
            ? null 
            : new CrossFade(System.TimeSpan.FromMilliseconds(200));
        PageHost.Content = page;
        ScheduleBead();
    }

    private void ScheduleBead()
    {
        Dispatcher.UIThread.Post(UpdateBead, DispatcherPriority.Loaded);
        DispatcherTimer.RunOnce(UpdateBead, System.TimeSpan.FromMilliseconds(230));
    }

    private void UpdateBead()
    {
        var active = this.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.Classes.Contains("nav") && b.Classes.Contains("active"));
        if (active == null || SpineCanvas == null || SpineBead == null) return;
        if (active.TranslatePoint(new Point(0, active.Bounds.Height / 2), SpineCanvas) is { } p)
            Canvas.SetTop(SpineBead, p.Y - 4);
    }
}