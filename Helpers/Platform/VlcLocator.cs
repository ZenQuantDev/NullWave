using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Serilog;

namespace NullWave.Helpers;

public enum VlcSource { Override, Registry, ProgramFiles, Bundled }

public sealed record VlcCandidate(string Directory, VlcSource Source);

/// <summary>The outcome of looking at one candidate folder. Reason is null when it is usable.</summary>
public sealed record VlcCheck(VlcCandidate Candidate, bool Usable, string? Reason);

public sealed record VlcResolution(VlcCandidate? Chosen, IReadOnlyList<VlcCheck> Checks)
{
    /// <summary>The folder to pass to LibVLC, or null when nothing usable was found.</summary>
    public string? Directory => Chosen?.Directory;
}

/// <summary>
/// Finds a VLC install that will actually load. A folder only counts if:
///   - libvlc.dll and libvlccore.dll are both there,
///   - the plugins folder exists and has plugin DLLs in it (a VLC without plugins loads and plays nothing),
///   - the DLL is built for the same CPU type as NullWave (a 32-bit VLC cannot load into a 64-bit app).
///
/// Where it looks, in order: the NULLWAVE_VLC_DIR environment variable, the registry entry VLC's
/// installer writes (64-bit view, then 32-bit view), the usual Program Files folders, and the copy of
/// libvlc that ships next to NullWave. The first usable folder wins.
/// </summary>
public static class VlcLocator
{
    public const ushort MachineX86 = 0x014C;
    public const ushort MachineX64 = 0x8664;
    public const ushort MachineArm64 = 0xAA64;

    private static readonly object Gate = new();
    private static VlcResolution? _cached;

    /// <summary>The cached answer for this run. Looked up once; call Invalidate() after installing VLC.</summary>
    public static VlcResolution Current
    {
        get
        {
            lock (Gate) return _cached ??= Resolve();
        }
    }

    public static void Invalidate()
    {
        lock (Gate) _cached = null;
    }

    internal static VlcResolution Resolve()
    {
        var overrideDir = Environment.GetEnvironmentVariable("NULLWAVE_VLC_DIR");
        var registry = OperatingSystem.IsWindows() ? ReadRegistryInstallDirs() : new List<string>();
        var programFiles = OperatingSystem.IsWindows() ? ProgramFilesDirs() : new List<string>();
        var bundled = OperatingSystem.IsWindows() ? BundledDir() : null;

        var result = Evaluate(BuildCandidates(overrideDir, registry, programFiles, bundled), CurrentProcessMachine());

        foreach (var check in result.Checks.Where(c => !c.Usable))
            Log.Debug("[VlcLocator] Skipped {Dir} ({Source}): {Reason}", check.Candidate.Directory, check.Candidate.Source, check.Reason);

        if (result.Chosen != null)
            Log.Information("[VlcLocator] Using {Source} VLC at {Dir}", result.Chosen.Source, result.Chosen.Directory);
        else
            Log.Warning("[VlcLocator] No usable VLC found. {Checked}",
                result.Checks.Count == 0
                    ? "Nothing to check on this platform."
                    : string.Join("; ", result.Checks.Select(c => $"{c.Candidate.Directory}: {c.Reason}")));

        return result;
    }

    /// <summary>Lists the folders to try, in priority order, with duplicates removed.</summary>
    internal static List<VlcCandidate> BuildCandidates(
        string? overrideDir,
        IEnumerable<string> registryDirs,
        IEnumerable<string> programFilesDirs,
        string? bundledDir)
    {
        var list = new List<VlcCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? dir, VlcSource source)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;

            string normalized;
            try
            {
                normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir.Trim().Trim('"')));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return;
            }

            if (seen.Add(normalized)) list.Add(new VlcCandidate(normalized, source));
        }

        Add(overrideDir, VlcSource.Override);
        foreach (var dir in registryDirs) Add(dir, VlcSource.Registry);
        foreach (var dir in programFilesDirs) Add(dir, VlcSource.ProgramFiles);
        Add(bundledDir, VlcSource.Bundled);

        return list;
    }

    /// <summary>Checks candidates in order and stops at the first usable one.</summary>
    internal static VlcResolution Evaluate(IEnumerable<VlcCandidate> candidates, ushort? processMachine)
    {
        var checks = new List<VlcCheck>();
        VlcCandidate? chosen = null;

        foreach (var candidate in candidates)
        {
            var reason = RejectReason(candidate.Directory, processMachine);
            checks.Add(new VlcCheck(candidate, reason == null, reason));
            if (reason == null)
            {
                chosen = candidate;
                break;
            }
        }

        return new VlcResolution(chosen, checks);
    }

    private static string? RejectReason(string dir, ushort? processMachine)
    {
        if (!Directory.Exists(dir)) return "folder does not exist";

        var libvlc = Path.Combine(dir, "libvlc.dll");
        if (!File.Exists(libvlc)) return "libvlc.dll is missing";
        if (!File.Exists(Path.Combine(dir, "libvlccore.dll"))) return "libvlccore.dll is missing";

        var plugins = Path.Combine(dir, "plugins");
        if (!Directory.Exists(plugins) || !HasAnyDll(plugins)) return "plugins folder is missing or empty";

        var machine = ReadMachine(libvlc);
        if (machine is null) return "libvlc.dll is not a valid Windows library";

        if (processMachine is ushort expected && machine.Value != expected)
            return $"libvlc.dll is {MachineName(machine.Value)} but NullWave is running as {MachineName(expected)}";

        return null;
    }

    private static bool HasAnyDll(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the CPU type from a Windows DLL's header (without loading it): 0x8664 = x64,
    /// 0x014C = x86, 0xAA64 = ARM64. Returns null if the file is not a Windows executable image.
    /// </summary>
    internal static ushort? ReadMachine(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            Span<byte> dosHeader = stackalloc byte[64];
            if (stream.Read(dosHeader) < 64) return null;
            if (dosHeader[0] != (byte)'M' || dosHeader[1] != (byte)'Z') return null;

            var peOffset = BitConverter.ToInt32(dosHeader.Slice(60, 4));   // e_lfanew
            if (peOffset < 64 || peOffset > 1_000_000) return null;

            stream.Position = peOffset;
            Span<byte> peHeader = stackalloc byte[6];
            if (stream.Read(peHeader) < 6) return null;
            if (peHeader[0] != (byte)'P' || peHeader[1] != (byte)'E' || peHeader[2] != 0 || peHeader[3] != 0) return null;

            return BitConverter.ToUInt16(peHeader.Slice(4, 2));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string MachineName(ushort machine) => machine switch
    {
        MachineX86 => "x86 (32-bit)",
        MachineX64 => "x64 (64-bit)",
        MachineArm64 => "ARM64",
        _ => $"unknown (0x{machine:X4})"
    };

    private static ushort? CurrentProcessMachine() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X86 => MachineX86,
        Architecture.X64 => MachineX64,
        Architecture.Arm64 => MachineArm64,
        _ => null
    };

    [SupportedOSPlatform("windows")]
    private static List<string> ReadRegistryInstallDirs()
    {
        var dirs = new List<string>();

        // 64-bit view first, then the 32-bit (WOW6432Node) view that a 32-bit VLC installer writes to.
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            string? value = null;
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(@"SOFTWARE\VideoLAN\VLC");
                value = key?.GetValue("InstallDir") as string;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // Registry not readable: fall through to the folder checks.
            }

            if (!string.IsNullOrWhiteSpace(value)) dirs.Add(value);
        }

        return dirs;
    }

    private static List<string> ProgramFilesDirs() => new()
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VideoLAN", "VLC"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VideoLAN", "VLC")
    };

    private static string? BundledDir()
    {
        var folder = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.X86 => "win-x86",
            Architecture.Arm64 => "win-arm64",
            _ => null
        };

        return folder == null ? null : Path.Combine(AppContext.BaseDirectory, "libvlc", folder);
    }
}