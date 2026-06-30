using System.Collections.Generic;
using System.Linq;

namespace NullWave.Services.SmartSorting;

public enum ModelTier { Tiny, Small, Medium, Large, XL }

/// <summary>
/// Catalogue entry for a supported Ollama model.
/// Used to populate the model picker UI with hardware-aware recommendations.
/// </summary>
public record AIModelEntry(
    string OllamaId,         // e.g. "qwen2.5:7b"
    string DisplayName,      // e.g. "Qwen 2.5 7B"
    ModelTier Tier,
    int ParametersBillions,  // 0.5, 1, 3, 7, 8, 12, 14, 32 ...
    int MinVramGB,           // minimum GPU VRAM to run comfortably
    int MinRamGB,            // minimum system RAM for CPU-only
    string Description)
{
    /// <summary>
    /// Human-readable size label shown in the UI.
    /// </summary>
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
    /// <summary>
    /// Full list of supported models, ordered from smallest to largest.
    /// OllamaId matches what you pass to "ollama pull <id>".
    /// </summary>
    public static readonly IReadOnlyList<AIModelEntry> All = new List<AIModelEntry>
    {
        //  Tiny (≤1B) - runs on anything 
        new("qwen2.5:0.5b",      "Qwen 2.5 0.5B",      ModelTier.Tiny,   0,  1,  2,
            "Fastest possible. Basic tagging only. Works on any device."),

        new("llama3.2:1b",       "Llama 3.2 1B",        ModelTier.Tiny,   1,  1,  2,
            "Meta's smallest Llama 3.2. Good for simple classifications."),

        new("phi3.5:mini",       "Phi-3.5 Mini (3.8B)", ModelTier.Tiny,   4,  2,  4,
            "Microsoft Phi-3.5. Surprisingly capable for its size."),

        //  Small (3-4B) - works on 4GB RAM+ 
        new("qwen2.5:3b",        "Qwen 2.5 3B",         ModelTier.Small,  3,  2,  4,
            "Good quality tagging. Recommended for devices with 4GB RAM."),

        new("llama3.2:3b",       "Llama 3.2 3B",        ModelTier.Small,  3,  2,  4,
            "Meta's Llama 3.2 3B. Strong reasoning for a small model."),

        new("gemma3:4b",         "Gemma 3 4B",           ModelTier.Small,  4,  3,  5,
            "Google Gemma 3. Great creative and music context understanding."),

        new("phi4:mini",         "Phi-4 Mini (3.8B)",   ModelTier.Small,  4,  3,  5,
            "Microsoft Phi-4 Mini. Best-in-class small model reasoning."),

        //  Medium (7-8B) - recommended for most users 
        new("qwen2.5:7b",        "Qwen 2.5 7B",         ModelTier.Medium, 7,  4,  8,
            "Best balance of quality and speed. Default recommendation."),

        new("llama3.1:8b",       "Llama 3.1 8B",        ModelTier.Medium, 8,  5,  8,
            "Meta's flagship 8B model. Excellent general purpose."),

        new("llama3.2:8b",       "Llama 3.2 8B",        ModelTier.Medium, 8,  5,  8,
            "Newer Llama 3.2 8B. Better instruction following than 3.1."),

        new("gemma3:9b",         "Gemma 3 9B",           ModelTier.Medium, 9,  5,  10,
            "Google Gemma 3 9B. Strong music and mood understanding."),

        new("phi4",              "Phi-4 (14B)",          ModelTier.Medium, 14, 8,  16,
            "Microsoft Phi-4 full. Exceptional quality, needs 8GB+ RAM."),

        new("deepseek-r1:7b",    "DeepSeek R1 7B",      ModelTier.Medium, 7,  4,  8,
            "DeepSeek R1 distilled. Excellent step-by-step reasoning."),

        //  Large (12-14B) - needs 8GB+ RAM or 8GB+ VRAM 
        new("mistral-nemo:12b",  "Mistral Nemo 12B",    ModelTier.Large,  12, 8,  12,
            "Mistral Nemo. Great multilingual support and music context."),

        new("qwen2.5:14b",       "Qwen 2.5 14B",        ModelTier.Large,  14, 8,  16,
            "High quality tagging. Needs 16GB RAM for CPU-only."),

        new("llama3.1:70b",      "Llama 3.1 70B",       ModelTier.Large,  70, 24, 48,
            "Very large. GPU with 24GB+ VRAM recommended."),

        //  XL (32B+) - workstation / high-end GPU only 
        new("qwen2.5:32b",       "Qwen 2.5 32B",        ModelTier.XL,     32, 20, 32,
            "Maximum quality. Requires 32GB RAM or 20GB+ VRAM."),

        new("deepseek-r1:32b",   "DeepSeek R1 32B",     ModelTier.XL,     32, 20, 32,
            "DeepSeek R1 32B. Best reasoning quality available locally."),

        new("gemma3:27b",        "Gemma 3 27B",          ModelTier.XL,     27, 16, 28,
            "Google Gemma 3 27B. Near-GPT-4 quality for local inference."),
    };

    /// <summary>
    /// Returns all models that can run on the given hardware without
    /// exceeding RAM/VRAM limits. GPU path takes priority over CPU path.
    /// </summary>
    public static IEnumerable<AIModelEntry> GetCompatible(
        long ramGB, long vramGB, bool hasGpu)
    {
        return All.Where(m => hasGpu
            ? vramGB >= m.MinVramGB
            : ramGB  >= m.MinRamGB);
    }

    /// <summary>
    /// Returns the OllamaId strings for all models, for use in ComboBox
    /// ItemsSource when hardware filtering is not applied.
    /// </summary>
    public static string[] AllIds => All.Select(m => m.OllamaId).ToArray();

    /// <summary>
    /// Gets the display label shown next to a model in the picker.
    /// Format: "Qwen 2.5 7B · Medium · 8GB RAM"
    /// </summary>
    public static string GetLabel(string ollamaId)
    {
        var entry = All.FirstOrDefault(m => m.OllamaId == ollamaId);
        if (entry == null) return ollamaId;
        return $"{entry.DisplayName} · {entry.TierLabel} · {entry.MinRamGB}GB RAM";
    }

    /// <summary>
    /// Suggests the best battery-safe model (Small tier, ≤4GB RAM).
    /// </summary>
    public static string SuggestBatteryModel(long ramGB)
    {
        return All
            .Where(m => m.Tier <= ModelTier.Small && m.MinRamGB <= ramGB)
            .OrderByDescending(m => m.ParametersBillions)
            .FirstOrDefault()?.OllamaId ?? "qwen2.5:3b";
    }

    /// <summary>
    /// Suggests the best performance model for the available hardware.
    /// </summary>
    public static string SuggestPerformanceModel(long ramGB, long vramGB, bool hasGpu)
    {
        return GetCompatible(ramGB, vramGB, hasGpu)
            .OrderByDescending(m => m.ParametersBillions)
            .FirstOrDefault()?.OllamaId ?? "qwen2.5:7b";
    }
}