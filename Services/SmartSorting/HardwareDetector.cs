using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Helpers;
using Serilog;

namespace NullWave.Services.SmartSorting;

public class HardwareDetector
{
    internal static Func<CancellationToken, Task<HardwareInfo>>? DetectFactory { get; set; }

    internal static void ResetCacheForTests()
    {
        lock (_cacheLock)
        {
            _cachedInfoTask = null;
        }
    }

    public static async Task<HardwareInfo> RefreshAsync()
    {
        lock (_cacheLock)
        {
            _cachedInfoTask = null;
        }
        return await GetCachedInfoAsync();
    }

    public async Task<HardwareInfo> DetectAsync(CancellationToken ct = default)
    {
        var info = new HardwareInfo
        {
            CpuCores = Environment.ProcessorCount,
            IsArm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
        };

        if (info.IsArm64)
        {
            info.HasAvx = true;
            info.HasAvx2 = true;
        }
        else
        {
            info.HasAvx = Avx.IsSupported;
            info.HasAvx2 = Avx2.IsSupported;
        }

        var ramTask = Task.Run(() => GetTotalRamGB(), ct);
        var gpuTask = GetGpuInfoAsync(info, ct);

        await Task.WhenAll(ramTask, gpuTask);
        info.RamGB = ramTask.Result;

        var (recommendedModel, reason) = AIModelCatalog.Recommend(
            info.RamGB, info.GpuVramGB, info.HasNvidia || info.HasAmd, 
            info.HasAvx, info.HasAvx2, info.IsArm64);
            
        info.RecommendedModel = recommendedModel;
        info.RecommendationReason = reason;

        return info;
    }

    private long GetTotalRamGB()
    {
        try
        {
            var memInfo = GC.GetGCMemoryInfo();
            long bytes = memInfo.TotalAvailableMemoryBytes;
            if (bytes > 0)
            {
                return (long)Math.Round((double)bytes / 1024.0 / 1024.0 / 1024.0);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[HardwareDetector] Failed to detect RAM via GC");
        }
        return 4; 
    }

    internal static bool IsModernGpuName(string n) =>
    !n.Contains("Basic Render", StringComparison.OrdinalIgnoreCase) &&
    !n.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) &&
    (new[] { "Iris", "UHD", " Xe", "Arc", "Radeon", "Vega", "GeForce", "RTX", "GTX", "Quadro", "Adreno" }
         .Any(k => n.Contains(k, StringComparison.OrdinalIgnoreCase))
     || System.Text.RegularExpressions.Regex.IsMatch(n, @"HD Graphics\s+[56]\d\d"));

    private static void DetectLinuxGpuVendors(HardwareInfo info)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories("/sys/class/drm", "card?"))
            {
                var f = Path.Combine(dir, "device", "vendor");
                if (!File.Exists(f)) continue;
                var v = File.ReadAllText(f).Trim().ToLowerInvariant();
                if (v is "0x10de" or "0x1002" or "0x8086") 
                    info.HasModernGpu = true;
            }
        }
        catch { }
    }

    private async Task GetGpuInfoAsync(HardwareInfo info, CancellationToken ct)
    {
        // 1. Try NVIDIA
        try
        {
            var result = await ProcessRunner.RunAsync(
                "nvidia-smi", 
                new[] { "--query-gpu=name,memory.total", "--format=csv,noheader,nounits" }, 
                TimeSpan.FromSeconds(5), ct);
                
            if (!result.TimedOut && !result.Canceled && result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                var parts = result.StandardOutput.Split(',');
                if (parts.Length >= 2)
                {
                    info.GpuType = parts[0].Trim();
                    info.HasNvidia = true;
                    if (long.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long mb))
                        info.GpuVramGB = (long)Math.Round((double)mb / 1024.0);
                    return;
                }
            }
        }
        catch { /* nvidia-smi not found */ }

        // 2. Try AMD (rocm-smi on Linux)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                var result = await ProcessRunner.RunAsync(
                    "rocm-smi", 
                    new[] { "--showmeminfo", "vram", "--csv" }, 
                    TimeSpan.FromSeconds(5), ct);
                    
                if (!result.TimedOut && !result.Canceled && result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
                {
                    info.GpuType = "AMD Radeon";
                    info.HasAmd = true;
                    info.GpuVramGB = 8; 
                }
            }
            catch { }
        }
        
        // 3. Fallback to WMI on Windows for AMD/Intel
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !info.HasNvidia)
        {
            try
            {
                var result = await ProcessRunner.RunAsync(
                    "powershell",
                    new[] { "-NoProfile", "-Command", "Get-CimInstance Win32_VideoController | Select-Object -Property Name, AdapterRAM | ConvertTo-Csv -NoTypeInformation" },
                    TimeSpan.FromSeconds(5), ct);

                if (!result.TimedOut && !result.Canceled && result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
                {
                    var lines = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    
                    foreach (var line in lines.Skip(1))
                    {
                        var parts = line.Split(',').Select(p => p.Trim('"', ' ')).ToArray();
                        if (parts.Length >= 2)
                        {
                            var name = parts[0];
                            if (string.IsNullOrWhiteSpace(name)) continue;

                            if (info.GpuName == "Unknown")
                                info.GpuName = name;

                            // Check if this is a modern GPU (Intel UHD/Iris, AMD Vega, etc.)
                            if (IsModernGpuName(name))
                                info.HasModernGpu = true;
                            
                            // FIX: Require discrete GPU signal to avoid integrated APUs for AI
                            bool isDiscreteAmd = name.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase) || 
                                                 name.Contains("Radeon Pro", StringComparison.OrdinalIgnoreCase);
                            bool isDiscreteIntel = name.Contains("Arc", StringComparison.OrdinalIgnoreCase);
                            bool isDiscreteNvidia = name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                                                    name.Contains("Quadro", StringComparison.OrdinalIgnoreCase) ||
                                                    name.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
                                                    name.Contains("GTX", StringComparison.OrdinalIgnoreCase);
                            
                            if (isDiscreteAmd || isDiscreteIntel || isDiscreteNvidia)
                            {
                                info.GpuType = name;
                                if (isDiscreteAmd) info.HasAmd = true;
                                if (isDiscreteNvidia) info.HasNvidia = true;
                                    
                                if (long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long vramBytes) && vramBytes >= 1073741824)
                                {
                                    info.GpuVramGB = Math.Max(4, (long)Math.Round((double)vramBytes / 1024.0 / 1024.0 / 1024.0));
                                }
                                else
                                {
                                    info.GpuVramGB = 4; 
                                }
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Warning(ex, "[HardwareDetector] Windows GPU detection failed"); }
        }

        // 4. Linux: detect modern GPU vendors from /sys/class/drm
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            DetectLinuxGpuVendors(info);
        }
    }

    private static Task<HardwareInfo>? _cachedInfoTask;
    private static readonly object _cacheLock = new();

    public static Task<HardwareInfo> GetCachedInfoAsync(CancellationToken ct = default)
    {
        if (_cachedInfoTask != null && !_cachedInfoTask.IsFaulted && !_cachedInfoTask.IsCanceled) 
            return _cachedInfoTask;
            
        lock (_cacheLock)
        {
            if (_cachedInfoTask != null && !_cachedInfoTask.IsFaulted && !_cachedInfoTask.IsCanceled) 
                return _cachedInfoTask;
                
            _cachedInfoTask = DetectFactory != null
                ? DetectFactory(ct)
                : new HardwareDetector().DetectAsync(ct);
            return _cachedInfoTask;
        }
    }

    /// <summary>
    /// Non-blocking read for UI startup / EffectsTierResolver.
    /// Returns null if detection hasn't finished yet.
    /// </summary>
    public static HardwareInfo? CachedOrNull()
    {
        if (_cachedInfoTask != null && _cachedInfoTask.IsCompletedSuccessfully)
            return _cachedInfoTask.Result;
        return null;
    }

    public static async Task<bool> SupportsFullEffectsTierAsync()
    {
        var info = await GetCachedInfoAsync();
        return info.HasNvidia || info.HasAmd;
    }
    
    /// <summary>
    /// Synchronous wrapper for EffectsTierResolver. Returns false (safe default) 
    /// if detection is still running, and triggers it in the background.
    /// </summary>
    public static bool SupportsFullEffectsTier()
    {
        var info = CachedOrNull();
        if (info != null) return info.HasNvidia || info.HasAmd;
        
        _ = GetCachedInfoAsync(); 
        return false;
    }
}