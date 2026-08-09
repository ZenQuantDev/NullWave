using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Serilog;

namespace NullWave.Services.SmartSorting;

public class HardwareInfo
{
    public int CpuCores { get; set; }
    public long RamGB { get; set; }
    public long GpuVramGB { get; set; }
    public string GpuType { get; set; } = "Unknown";
    public bool HasNvidia { get; set; }
    public bool HasAmd { get; set; }
    public string RecommendedModel { get; set; } = "qwen2.5:7b";
    public string RecommendationReason { get; set; } = string.Empty;
}

public class HardwareDetector
{
    public HardwareInfo Detect()
    {
        var info = new HardwareInfo
        {
            CpuCores = Environment.ProcessorCount,
            RamGB = GetTotalRamGB()
        };

        GetGpuInfo(info);

        var (recommendedModel, reason) = RecommendModel(
            info.CpuCores, info.RamGB, info.GpuVramGB, info.HasNvidia, info.HasAmd);

        info.RecommendedModel = recommendedModel;
        info.RecommendationReason = reason;

        return info;
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
                            return (long)Math.Round((double)kb / 1024.0 / 1024.0);
                        }
                    }
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Use PowerShell instead of deprecated wmic for Win 11 compatibility
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-NoProfile -Command \"(Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null && proc.WaitForExit(3000))
                {
                    var output = proc.StandardOutput.ReadToEnd().Trim();
                    if (long.TryParse(output, out long bytes))
                    {
                        return (long)Math.Round((double)bytes / 1024.0 / 1024.0 / 1024.0);
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

    private void GetGpuInfo(HardwareInfo info)
    {
        // 1. Try NVIDIA (Works on both Linux and Windows if drivers are installed)
        try
        {
            var psi = new ProcessStartInfo("nvidia-smi", "--query-gpu=name,memory.total --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                if (proc.WaitForExit(3000))
                {
                    var output = proc.StandardOutput.ReadToEnd();
                    if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    {
                        var parts = output.Split(',');
                        if (parts.Length >= 2)
                        {
                            info.GpuType = parts[0].Trim();
                            info.HasNvidia = true;
                            if (long.TryParse(parts[1].Trim(), out long mb))
                                info.GpuVramGB = (long)Math.Round((double)mb / 1024.0);
                        }
                    }
                }
                else
                {
                    Log.Warning("[HardwareDetector] nvidia-smi timed out after 3 seconds.");
                    proc.Kill();
                }
            }
        }
        catch { /* nvidia-smi not found */ }

        if (info.HasNvidia) return;

        // 2. Try AMD / Intel (OS-specific)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                var psi = new ProcessStartInfo("rocm-smi", "--showmeminfo vram --csv")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    if (proc.WaitForExit(3000))
                    {
                        var output = proc.StandardOutput.ReadToEnd();
                        if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                        {
                            info.GpuType = "AMD Radeon";
                            info.HasAmd = true;
                            info.GpuVramGB = 8;
                        }
                    }
                    else
                    {
                        Log.Warning("[HardwareDetector] rocm-smi timed out after 3 seconds.");
                        proc.Kill();
                    }
                }
            }
            catch { }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Use WMI via PowerShell to find AMD/Intel GPUs on Windows
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-NoProfile -Command \"Get-CimInstance Win32_VideoController | Select-Object -Property Name, AdapterRAM | ConvertTo-Csv -NoTypeInformation\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null && proc.WaitForExit(3000))
                {
                    var output = proc.StandardOutput.ReadToEnd();
                    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines.Skip(1)) // Skip CSV header
                    {
                        var parts = line.Split(',').Select(p => p.Trim('"', ' ')).ToArray();
                        if (parts.Length >= 2)
                        {
                            var name = parts[0];
                            if (string.IsNullOrWhiteSpace(name)) continue;

                            info.GpuType = name;
                            if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                                info.HasAmd = true;

                            if (long.TryParse(parts[1], out long vramBytes))
                                info.GpuVramGB = (long)Math.Round((double)vramBytes / 1024.0 / 1024.0 / 1024.0);

                            break; // Take the first dedicated GPU found
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Warning(ex, "[HardwareDetector] Windows GPU detection failed"); }
        }
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