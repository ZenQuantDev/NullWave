using System;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Serilog;

namespace NullWave.Services.SmartSorting;

public class HardwareInfo
{
    public int CpuCores { get; init; }
    public long RamGB { get; init; }
    public long GpuVramGB { get; init; }
    public string GpuType { get; init; } = "Unknown";
    public bool HasNvidia { get; init; }
    public bool HasAmd { get; init; }
    public string RecommendedModel { get; init; } = "qwen2.5:7b";
    public string RecommendationReason { get; init; } = string.Empty;
}

public class HardwareDetector
{
    public HardwareInfo Detect()
    {
        var cpuCores = Environment.ProcessorCount;
        var ramGB = GetTotalRamGB();
        var (gpuVram, gpuType, hasNvidia, hasAmd) = GetGpuInfo();
        var (recommendedModel, reason) = RecommendModel(cpuCores, ramGB, gpuVram, hasNvidia, hasAmd);

        return new HardwareInfo
        {
            CpuCores = cpuCores,
            RamGB = ramGB,
            GpuVramGB = gpuVram,
            GpuType = gpuType,
            HasNvidia = hasNvidia,
            HasAmd = hasAmd,
            RecommendedModel = recommendedModel,
            RecommendationReason = reason
        };
    }

    private long GetTotalRamGB()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var memInfo = File.ReadAllText("/proc/meminfo");
                foreach (var line in memInfo.Split('\n'))
                {
                    if (line.StartsWith("MemTotal:"))
                    {
                        var parts = line.Split(':', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            var kb = long.Parse(new string(parts[1].Where(char.IsDigit).ToArray()));
                            return kb / 1024 / 1024; // Convert KB to GB
                        }
                    }
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "wmic",
                    Arguments = "OS get TotalVisibleMemorySize /Value",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();
                    foreach (var line in output.Split('\n'))
                    {
                        if (line.Contains("TotalVisibleMemorySize="))
                        {
                            var kb = long.Parse(line.Split('=')[1].Trim());
                            return kb / 1024 / 1024;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[HardwareDetector] Failed to detect RAM");
        }
        return 8; // Default fallback
    }

    private (long vramGB, string gpuType, bool hasNvidia, bool hasAmd) GetGpuInfo()
    {
        long vram = 0;
        string gpuType = "Unknown";
        bool hasNvidia = false;
        bool hasAmd = false;

        // Try nvidia-smi first
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=memory.total,name --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                RedirectStandardError = true, // FIX: Capture stderr to prevent driver error noise
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    hasNvidia = true;
                    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length > 0)
                    {
                        var parts = lines[0].Split(',');
                        if (parts.Length >= 2)
                        {
                            vram = long.Parse(parts[0].Trim()) / 1024; // MB to GB
                            gpuType = parts[1].Trim();
                        }
                    }
                }
            }
        }
        catch
        {
            // nvidia-smi not available
        }

        // Try rocm-smi for AMD
        if (!hasNvidia)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "rocm-smi",
                    Arguments = "--showmeminfo vram",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true, // FIX: Capture stderr to prevent driver error noise
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();
                    if (proc.ExitCode == 0 && output.Contains("VRAM Total"))
                    {
                        hasAmd = true;
                        gpuType = "AMD GPU";
                        foreach (var line in output.Split('\n'))
                        {
                            if (line.Contains("VRAM Total") && line.Contains("GB"))
                            {
                                var parts = line.Split(' ');
                                foreach (var part in parts)
                                {
                                    if (double.TryParse(part, out double gb))
                                    {
                                        vram = (long)gb;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // rocm-smi not available
            }
        }

        return (vram, gpuType, hasNvidia, hasAmd);
    }

    private (string model, string reason) RecommendModel(int cpuCores, long ramGB, long gpuVram, bool hasNvidia, bool hasAmd)
    {
        // GPU-accelerated models (fastest)
        if (gpuVram >= 24 && (hasNvidia || hasAmd))
            return ("qwen2.5:32b", $"32GB VRAM detected ({gpuVram}GB) - can run 32B model with GPU acceleration");
        if (gpuVram >= 16 && (hasNvidia || hasAmd))
            return ("qwen2.5:14b", $"16GB VRAM detected ({gpuVram}GB) - can run 14B model with GPU acceleration");
        if (gpuVram >= 8 && (hasNvidia || hasAmd))
            return ("mistral-nemo:12b", $"8GB VRAM detected ({gpuVram}GB) - can run 12B model with GPU acceleration");
        if (gpuVram >= 4 && (hasNvidia || hasAmd))
            return ("qwen2.5:7b", $"4GB VRAM detected ({gpuVram}GB) - can run 7B model with GPU acceleration");

        // CPU-only models (slower but works)
        // FIX: Interpolate actual ramGB variable instead of hardcoded "8GB" strings
        if (ramGB >= 32)
            return ("qwen2.5:32b", $"{ramGB}GB RAM detected - can run 32B model (CPU-only, slower)");
        if (ramGB >= 16)
            return ("qwen2.5:14b", $"{ramGB}GB RAM detected - can run 14B model (CPU-only, slower)");
        if (ramGB >= 8)
            return ("mistral-nemo:12b", $"{ramGB}GB RAM detected - can run 12B model (CPU-only, slower)");
        if (ramGB >= 4)
            return ("qwen2.5:7b", $"{ramGB}GB RAM detected - can run 7B model (CPU-only, slower)");

        // Fallback
        return ("qwen2.5:3b", $"Limited hardware ({ramGB}GB RAM) - using smallest model for best performance");
    }
}