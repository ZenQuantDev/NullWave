using System;
using System.Collections.Generic;
using System.Linq;

namespace NullWave.Services.SmartSorting;

public enum ModelTier { Tiny, Small, Medium, Large, XL }

public record AIModelEntry(
    string OllamaId,         
    string DisplayName,      
    ModelTier Tier,
    int ParametersBillions,  
    double WeightSizeGB,     
    string Description)
{
    public string TierLabel => Tier switch
    {
        ModelTier.Tiny   => "Tiny",
        ModelTier.Small  => "Small",
        ModelTier.Medium => "Medium",
        ModelTier.Large  => "Large",
        ModelTier.XL     => "XL",
        _                => "?"
    };
}

public static class AIModelCatalog
{
    public static readonly IReadOnlyList<AIModelEntry> All = new List<AIModelEntry>
    {
        // Tiny
        new("qwen2.5:0.5b", "Qwen 2.5 0.5B", ModelTier.Tiny, 0, 0.4, "Fastest possible. Basic tagging only."),
        new("llama3.2:1b",  "Llama 3.2 1B",   ModelTier.Tiny, 1, 0.7, "Meta's smallest Llama 3.2."),
        
        // Small
        new("qwen2.5:3b",    "Qwen 2.5 3B",    ModelTier.Small, 3, 1.9, "Good quality tagging."),
        new("llama3.2:3b",   "Llama 3.2 3B",   ModelTier.Small, 3, 2.0, "Meta's Llama 3.2 3B."),
        new("gemma3:4b",     "Gemma 3 4B",     ModelTier.Small, 4, 2.6, "Google Gemma 3 4B."),
        new("phi4-mini",     "Phi-4 Mini",     ModelTier.Small, 4, 2.3, "Microsoft Phi-4 Mini."), // Moved from Tiny
        
        // Medium
        new("qwen2.5:7b",    "Qwen 2.5 7B",    ModelTier.Medium, 7, 4.7, "Best balance of quality and speed."),
        new("llama3.1:8b",   "Llama 3.1 8B",   ModelTier.Medium, 8, 5.4, "Meta's flagship 8B model."),
        new("gemma3:12b",    "Gemma 3 12B",    ModelTier.Medium, 12, 7.1, "Google Gemma 3 12B."),
        new("phi4",          "Phi-4 14B",      ModelTier.Medium, 14, 9.0, "Microsoft Phi-4 full."),
        new("deepseek-r1:7b","DeepSeek R1 7B", ModelTier.Medium, 7, 4.7, "DeepSeek R1 distilled."),
        
        // Large
        new("mistral-nemo:12b","Mistral Nemo 12B", ModelTier.Large, 12, 7.1, "Mistral Nemo."),
        new("qwen2.5:14b",   "Qwen 2.5 14B",   ModelTier.Large, 14, 9.0, "High quality tagging."),
        
        // XL
        new("qwen2.5:32b",   "Qwen 2.5 32B",   ModelTier.XL, 32, 20.0, "Maximum quality."),
        new("deepseek-r1:32b","DeepSeek R1 32B", ModelTier.XL, 32, 20.0, "DeepSeek R1 32B."),
        new("gemma3:27b",    "Gemma 3 27B",    ModelTier.XL, 27, 16.0, "Google Gemma 3 27B.")
    };

    private static readonly string[] RecommendedLadder = {
        "qwen2.5:0.5b", "qwen2.5:3b", "qwen2.5:7b", "qwen2.5:14b", "qwen2.5:32b"
    };

    public static string[] AllIds => All.Select(m => m.OllamaId).ToArray();

    private const long MinUsefulVramGB = 4;

    public static (string? model, string reason) Recommend(long ramGB, long vramGB, bool hasGpu, bool hasAvx, bool hasAvx2, bool isArm64)
    {
        if (!hasAvx && !isArm64)
            return (null, "CPU lacks AVX support - AI features not recommended");

        bool speedCapped = hasAvx && !hasAvx2 && !isArm64;
        var ladderModels = All.Where(m => RecommendedLadder.Contains(m.OllamaId)).ToList();

        if (speedCapped)
            ladderModels = ladderModels.Where(m => m.ParametersBillions <= 3).ToList();

        // FIX: Only use GPU path if VRAM is actually useful (>= 4GB)
        if (hasGpu && vramGB >= MinUsefulVramGB)
        {
            var best = ladderModels
                .Where(m => m.WeightSizeGB <= vramGB * 0.85)
                .OrderByDescending(m => m.ParametersBillions)
                .FirstOrDefault();
                
            if (best != null) return (best.OllamaId, $"Fits in {vramGB}GB VRAM (85% budget)");
        }
        
        // CPU Path (or fallback if GPU path found nothing / VRAM too small)
        if (ramGB < 7) ladderModels = ladderModels.Where(m => m.Tier == ModelTier.Tiny).ToList();
        else if (ramGB < 15) ladderModels = ladderModels.Where(m => m.Tier <= ModelTier.Small).ToList();
        else if (ramGB < 31) ladderModels = ladderModels.Where(m => m.Tier <= ModelTier.Medium).ToList();
        else if (ramGB < 63) ladderModels = ladderModels.Where(m => m.Tier <= ModelTier.Large).ToList();
        
        var bestCpu = ladderModels
            .Where(m => m.WeightSizeGB <= ramGB * 0.50)
            .OrderByDescending(m => m.ParametersBillions)
            .FirstOrDefault();
            
        if (bestCpu != null) return (bestCpu.OllamaId, $"Fits in {ramGB}GB RAM (50% budget)");

        var smallest = All.OrderBy(m => m.WeightSizeGB).First();
        return (smallest.OllamaId, "Hardware limited - using smallest available model");
    }

    public static string? SuggestPerformanceModel(long ramGB, long vramGB, bool hasGpu, bool hasAvx, bool hasAvx2, bool isArm64)
    {
        var (model, _) = Recommend(ramGB, vramGB, hasGpu, hasAvx, hasAvx2, isArm64);
        return model;
    }

    public static string SuggestBatteryModel(long ramGB)
    {
        var candidates = All.Where(m => m.Tier <= ModelTier.Small && m.WeightSizeGB <= ramGB * 0.50)
                            .OrderByDescending(m => m.ParametersBillions)
                            .FirstOrDefault();
        return candidates?.OllamaId ?? "qwen2.5:0.5b";
    }
}