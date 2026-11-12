using System;
using System.Reflection;

namespace NullWave.Helpers;

/// <summary>Single source of truth for the running host version.</summary>
public static class AppVersionInfo
{
    public static string Current { get; } = Resolve();

    private static string Resolve()
    {
        var assembly = typeof(AppVersionInfo).Assembly;
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            if (plus > 0) info = info[..plus];
            return info;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    /// <summary>True if hostVersion >= minVersion. Prerelease suffixes are ignored.</summary>
    public static bool Satisfies(string hostVersion, string minVersion)
    {
        static (int, int, int) Parse(string version)
        {
            var parts = version.Split('-', 2)[0].Split('.');
            int Get(int index) => index < parts.Length && int.TryParse(parts[index], out var value) ? value : 0;
            return (Get(0), Get(1), Get(2));
        }

        return Parse(hostVersion).CompareTo(Parse(minVersion)) >= 0;
    }
}
