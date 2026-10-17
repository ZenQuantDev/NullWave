using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes; 
using Avalonia.Input.Platform;
using Avalonia.Threading;
using NullWave.Models;
using NullWave.Services;
using Serilog;

namespace NullWave.Helpers;

/// <summary>
/// Centralized error reporting: logs the full exception to Serilog and shows
/// a user-friendly toast with a "Copy Details" button that copies the full
/// stack trace to the clipboard for bug reports.
/// 
/// Replaces scattered patterns like:
///   catch (Exception ex) { ToastService.Instance.Show($"Failed: {ex.Message}", Error); }
/// 
/// With:
///   catch (Exception ex) { ErrorReporter.Report(ex, "Backup failed. Please try again.", "backup"); }
/// </summary>
public static class ErrorReporter
{
    /// <summary>
    /// Report an exception to the user and logs.
    /// </summary>
    /// <param name="ex">The exception that occurred</param>
    /// <param name="userMessage">Friendly message shown to user (no technical details)</param>
    /// <param name="scope">Optional scope for toast grouping (e.g., "backup", "export", "download")</param>
    public static void Report(Exception ex, string userMessage, string? scope = null)
    {
        // 1. Log the full exception with context
        Log.Error(ex, "[ErrorReporter] {UserMessage} (scope={Scope})", userMessage, scope ?? "general");

        // 2. Capture the full exception text for clipboard
        var details = ex.ToString();

        // 3. Show toast with "Copy Details" action
        void ShowToast()
        {
            ToastService.Instance.Show(
                message: userMessage,
                type: ToastType.Error,
                durationMs: 8000,
                detailedMessage: details,
                actionText: "Copy Details",
                actionCallback: () => CopyToClipboard(details),
                scope: scope ?? "error",
                priority: ToastPriority.Normal
            );
        }

        // Marshal to UI thread if needed
        if (Dispatcher.UIThread.CheckAccess())
            ShowToast();
        else
            Dispatcher.UIThread.Post(ShowToast);
    }

    /// <summary>
    /// Report an error without an exception (for validation failures, etc.)
    /// </summary>
    public static void Report(string userMessage, string? scope = null, string? details = null)
    {
        Log.Warning("[ErrorReporter] {UserMessage} (scope={Scope})", userMessage, scope ?? "general");

        void ShowToast()
        {
            ToastService.Instance.Show(
                message: userMessage,
                type: ToastType.Error,
                durationMs: 6000,
                detailedMessage: details ?? string.Empty,
                scope: scope ?? "error"
            );
        }

        if (Dispatcher.UIThread.CheckAccess())
            ShowToast();
        else
            Dispatcher.UIThread.Post(ShowToast);
    }

    private static async void CopyToClipboard(string text)
    {
        try
        {
            var clipboard = GetClipboard();
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(text);
                ToastService.Instance.Show(
                    "Error details copied to clipboard.",
                    ToastType.Success,
                    durationMs: 2000,
                    scope: "clipboard"
                );
            }
            else
            {
                ToastService.Instance.Show(
                    "Could not access clipboard.",
                    ToastType.Warning,
                    durationMs: 2000,
                    scope: "clipboard"
                );
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ErrorReporter] Failed to copy to clipboard");
        }
    }

    private static IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow != null)
        {
            return TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard;
        }
        return null;
    }
}