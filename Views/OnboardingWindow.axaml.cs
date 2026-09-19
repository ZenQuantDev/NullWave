using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NullWave.ViewModels;
using Serilog;

namespace NullWave.Views;

public partial class OnboardingWindow : Window
{
    private const int MaxStep = 5;

    public static readonly DirectProperty<OnboardingWindow, int> CurrentStepProperty =
        AvaloniaProperty.RegisterDirect<OnboardingWindow, int>(
            nameof(CurrentStep), o => o.CurrentStep);

    public static readonly DirectProperty<OnboardingWindow, int> CurrentStepIndexProperty =
        AvaloniaProperty.RegisterDirect<OnboardingWindow, int>(
            nameof(CurrentStepIndex), o => o.CurrentStepIndex);

    public static readonly DirectProperty<OnboardingWindow, bool> IsLastStepProperty =
        AvaloniaProperty.RegisterDirect<OnboardingWindow, bool>(
            nameof(IsLastStep), o => o.IsLastStep);

    private int _step = 1;
    private int _stepIndex = 0;
    private bool _isLastStep = false;

    public int CurrentStep => _step;
    public int CurrentStepIndex => _stepIndex;
    public bool IsLastStep => _isLastStep;

    public static bool IsMacOs => OperatingSystem.IsMacOS();
    public static bool IsFedora { get; } = DetectFedora();

    private static bool DetectFedora()
    {
        try
        {
            if (OperatingSystem.IsLinux() && File.Exists("/etc/os-release"))
            {
                var osRelease = File.ReadAllText("/etc/os-release");
                return osRelease.Contains("ID=fedora", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[Onboarding] Could not read /etc/os-release");
        }
        return false;
    }

    public string OsGuideLabel =>
        OperatingSystem.IsWindows() ? "Windows" :
        IsMacOs                     ? "macOS" :
        IsFedora                    ? "Fedora Linux" : "Linux";

    // ---- Unattended CLI commands incorporating silent flags & RPM fusion swaps ----

    public string YtDlpInstallCmd =>
        OperatingSystem.IsWindows() ? "winget install --id yt-dlp.yt-dlp -e --silent --accept-package-agreements --accept-source-agreements" :
        IsFedora                    ? "sudo dnf install -y yt-dlp" :
                                      "sudo <pkg-mgr> install yt-dlp";

    public string VlcInstallCmd =>
        OperatingSystem.IsWindows() ? "winget install --id VideoLAN.VLC -e --silent --accept-package-agreements --accept-source-agreements" :
        IsFedora                    ? "sudo dnf install -y https://mirrors.rpmfusion.org/free/fedora/rpmfusion-free-release-$(rpm -E %fedora).rpm && sudo dnf install -y vlc" :
                                      "sudo <pkg-mgr> install vlc";

    public string FfmpegInstallCmd =>
        OperatingSystem.IsWindows() ? "winget install --id Gyan.FFmpeg -e --silent --accept-package-agreements --accept-source-agreements" :
        IsFedora                    ? "sudo dnf install -y https://mirrors.rpmfusion.org/free/fedora/rpmfusion-free-release-$(rpm -E %fedora).rpm && sudo dnf swap -y ffmpeg-free ffmpeg --allowerasing" :
                                      "sudo <pkg-mgr> install ffmpeg";

    public string OllamaInstallCmd =>
        OperatingSystem.IsWindows() ? "winget install --id Ollama.Ollama -e --silent --accept-package-agreements --accept-source-agreements" :
                                      "curl -fsSL https://ollama.com/install.sh | sh";

    public string Aria2InstallCmd =>
        OperatingSystem.IsWindows() ? "winget install --id aria2.aria2 -e --silent --accept-package-agreements --accept-source-agreements" :
        IsFedora                    ? "sudo dnf install -y aria2" :
                                      "sudo <pkg-mgr> install aria2";

    public OnboardingWindow()
    {
        InitializeComponent();
        AttachDragDropHandlers();
        UpdateStepUI();
    }

    public OnboardingWindow(SettingsViewModel settings) : this()
    {
        DataContext = settings;

        var osLang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var match = settings.SupportedLanguages.FirstOrDefault(kv =>
            kv.Key.StartsWith(osLang, StringComparison.OrdinalIgnoreCase));

        if (match.Key is not null && settings.SelectedLanguage != match.Key)
            settings.SelectedLanguage = match.Key;

        settings.CheckDependenciesCommand.Execute(null);

        UpdateStepUI();
    }

    private void AttachDragDropHandlers()
    {
        if (FolderDropZone == null) return;

        FolderDropZone.AddHandler(DragDrop.DragEnterEvent, OnFolderDragEnter);
        FolderDropZone.AddHandler(DragDrop.DragLeaveEvent, OnFolderDragLeave);
        FolderDropZone.AddHandler(DragDrop.DropEvent, OnFolderDrop);
    }

    private async void OnCopyInstallCmd(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string cmd }) return;
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(cmd);
                Log.Information("[Onboarding] Copied install command: {Cmd}", cmd);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Onboarding] Failed to copy install command");
        }
    }

    private void OnBack(object? sender, RoutedEventArgs e)
    {
        if (_step > 1) ShowStep(_step - 1);
    }

    private void OnNext(object? sender, RoutedEventArgs e)
    {
        if (_step == 3)
            (DataContext as SettingsViewModel)?.SaveKeysCommand.Execute(null);

        if (_step == MaxStep)
        {
            (DataContext as SettingsViewModel)?.CompleteOnboardingCommand.Execute(null);
            Close();
            return;
        }

        ShowStep(_step + 1);
    }

    private void OnSkip(object? sender, RoutedEventArgs e)
    {
        (DataContext as SettingsViewModel)?.CompleteOnboardingCommand.Execute(null);
        Close();
    }

    private void ShowStep(int step)
    {
        var clamped = Math.Clamp(step, 1, MaxStep);

        SetAndRaise(CurrentStepProperty, ref _step, clamped);
        SetAndRaise(CurrentStepIndexProperty, ref _stepIndex, clamped - 1);
        SetAndRaise(IsLastStepProperty, ref _isLastStep, clamped == MaxStep);

        UpdateStepUI();
    }

    private void UpdateStepUI()
    {
        if (BackButton != null) BackButton.IsEnabled = _step > 1;
        if (SkipButton != null) SkipButton.IsVisible = _step < MaxStep;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox) return;

        switch (e.Key)
        {
            case Key.Enter:
                OnNext(null, null!);
                e.Handled = true;
                break;
            case Key.Escape when _step < MaxStep:
                OnSkip(null, null!);
                e.Handled = true;
                break;
            case Key.Left when _step > 1:
                OnBack(null, null!);
                e.Handled = true;
                break;
            case Key.Right when _step < MaxStep:
                OnNext(null, null!);
                e.Handled = true;
                break;
        }
    }

    private void OnFolderDragEnter(object? sender, DragEventArgs e)
    {
        if (sender is Border border) border.Classes.Add("drop-target");
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnFolderDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Border border) border.Classes.Remove("drop-target");
        e.Handled = true;
    }

    private void OnFolderDrop(object? sender, DragEventArgs e)
    {
        if (sender is Border border) border.Classes.Remove("drop-target");

        try
        {
            var items = e.DataTransfer.TryGetFiles();
            if (items == null) return;

            string? selectedPath = null;
            foreach (var item in items)
            {
                var localPath = item.Path.LocalPath;
                if (string.IsNullOrWhiteSpace(localPath)) continue;

                if (item is IStorageFolder || Directory.Exists(localPath))
                {
                    selectedPath = localPath;
                    break;
                }
                if (File.Exists(localPath))
                {
                    selectedPath = Path.GetDirectoryName(localPath);
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(selectedPath) && DataContext is SettingsViewModel vm)
                vm.DownloadDirectory = selectedPath;

            e.Handled = true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[OnboardingWindow] Drag & drop folder resolution failed");
        }
    }
}