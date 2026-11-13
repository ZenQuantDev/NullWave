using System;
using System.Globalization;

namespace NullWave.Services;

public enum DependencyState { Unknown, Checking, Missing, Ready, UpToDate, UpdateAvailable, Busy, Failed }

public static class DependencyStatus
{
    public static DependencyState ClassifyYtDlp(DependencyInfo info)
    {
        if (!info.IsInstalled) return DependencyState.Missing;
        var latest = info.LatestVersion;
        if (string.IsNullOrWhiteSpace(latest) || latest == "unknown") return DependencyState.Ready;
        if (Version.TryParse(info.InstalledVersion, out var have) && Version.TryParse(latest, out var want))
            return have >= want ? DependencyState.UpToDate : DependencyState.UpdateAvailable;
        return string.Equals(info.InstalledVersion, latest, StringComparison.OrdinalIgnoreCase)
            ? DependencyState.UpToDate : DependencyState.UpdateAvailable;
    }

    public static string Text(DependencyState state, string? version, string? latest, Func<string, string> loc)
    {
        static string F(string format, params object?[] args) => string.Format(CultureInfo.InvariantCulture, format, args);
        return state switch
        {
            DependencyState.Checking => loc("Settings_Updates_Dep_Checking"),
            DependencyState.Missing => loc("Settings_Updates_Dep_Missing"),
            DependencyState.Ready => version ?? string.Empty,
            DependencyState.UpToDate => F(loc("Settings_Updates_Dep_UpToDate"), version),
            DependencyState.UpdateAvailable => F(loc("Settings_Updates_Dep_UpdateAvailable"), version, latest),
            DependencyState.Busy => loc("Settings_Updates_Dep_Busy"),
            DependencyState.Failed => string.IsNullOrEmpty(version)
                ? loc("Settings_Updates_Dep_Failed") : F(loc("Settings_Updates_Dep_FailedWith"), version),
            _ => string.Empty
        };
    }
}