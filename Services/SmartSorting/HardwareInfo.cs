using System;

namespace NullWave.Services.SmartSorting;

public class HardwareInfo
{
    public int CpuCores { get; set; }
    public long RamGB { get; set; }
    public long GpuVramGB { get; set; }
    public string GpuType { get; set; } = "Unknown";
    
    public bool HasNvidia { get; set; }
    public bool HasAmd { get; set; }
    public bool HasAvx { get; set; }
    public bool HasAvx2 { get; set; }
    public bool IsArm64 { get; set; }
    
    public string? RecommendedModel { get; set; }
    public string RecommendationReason { get; set; } = string.Empty;
}