using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Serilog;

namespace NullWave.Helpers;

/// <summary>
/// Version-agnostic clipboard text reader.
/// Bypasses compile-time CS1061 errors caused by Avalonia version differences
/// by probing the available read members via reflection at runtime.
/// </summary>
public static class ClipboardTextReader
{
    public static async Task<string?> TryGetTextAsync(IClipboard? clipboard)
    {
        if (clipboard == null) return null;

        try
        {
            var type = clipboard.GetType();

            // Probe 1: GetTextAsync() (Standard Avalonia 11.x)
            var getTextAsync = type.GetMethod("GetTextAsync", Type.EmptyTypes);
            if (getTextAsync != null)
            {
                if (getTextAsync.Invoke(clipboard, null) is Task task)
                {
                    await task.ConfigureAwait(false);
                    var resultProp = task.GetType().GetProperty("Result");
                    if (resultProp?.GetValue(task) is string s1) return s1;
                }
            }

            // Probe 2: GetDataAsync(string format) (Alternative/Fallback)
            var getDataAsync = type.GetMethod("GetDataAsync", new[] { typeof(string) });
            if (getDataAsync != null)
            {
                if (getDataAsync.Invoke(clipboard, new object[] { "Text" }) is Task task)
                {
                    await task.ConfigureAwait(false);
                    var resultProp = task.GetType().GetProperty("Result");
                    if (resultProp?.GetValue(task) is string s2) return s2;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[ClipboardTextReader] Reflection probe failed or returned non-string.");
        }

        return null;
    }
}