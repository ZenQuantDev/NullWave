using System;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NullWave.Helpers.Logging;
using Serilog.Events;

namespace NullWave.ViewModels.Settings;

/// <summary>
/// Backs the in-app log viewer in the Help tab: filters the InAppLogSink buffer
/// by text and level, and assembles a copy-pasteable diagnostics package.
/// </summary>
public partial class LogViewerViewModel : ObservableObject, IDisposable
{
    private readonly Func<string> _settingsSummary;

    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private bool _showDebug;
    [ObservableProperty] private bool _showInfo = true;
    [ObservableProperty] private bool _showWarning = true;
    [ObservableProperty] private bool _showError = true;
    [ObservableProperty] private string _filteredLog = string.Empty;

    public LogViewerViewModel(bool showDebugInitial, Func<string> settingsSummary)
    {
        _showDebug = showDebugInitial;
        _settingsSummary = settingsSummary;
        Refresh();
        InAppLogSink.LogUpdated += OnLogUpdated;
    }

    public void Dispose()
    {
        InAppLogSink.LogUpdated -= OnLogUpdated;
        GC.SuppressFinalize(this);
    }

    private void OnLogUpdated()
    {
        if (Dispatcher.UIThread.CheckAccess()) Refresh();
        else Dispatcher.UIThread.Post(Refresh);
    }

    partial void OnFilterTextChanged(string value) => Refresh();
    partial void OnShowDebugChanged(bool value) => Refresh();
    partial void OnShowInfoChanged(bool value) => Refresh();
    partial void OnShowWarningChanged(bool value) => Refresh();
    partial void OnShowErrorChanged(bool value) => Refresh();

    private bool IsLevelVisible(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose or LogEventLevel.Debug => ShowDebug,
        LogEventLevel.Information => ShowInfo,
        LogEventLevel.Warning => ShowWarning,
        LogEventLevel.Error or LogEventLevel.Fatal => ShowError,
        _ => true
    };

    private void Refresh()
    {
        var filter = FilterText.Trim();
        var sb = new StringBuilder();
        foreach (var entry in InAppLogSink.GetEntries())
        {
            if (!IsLevelVisible(entry.Level)) continue;
            if (filter.Length > 0 && !entry.Line.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            sb.AppendLine(entry.Line);
        }
        FilteredLog = sb.ToString().TrimEnd();
    }

    /// <summary>Full diagnostics package for bug reports: settings (no keys) + unfiltered log tail.</summary>
    public string BuildDiagnosticsText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== NullWave Diagnostics ===");
        sb.Append(_settingsSummary());
        sb.AppendLine("=== Recent log (unfiltered) ===");
        sb.AppendLine(InAppLogSink.GetSnapshot());
        return sb.ToString();
    }
}