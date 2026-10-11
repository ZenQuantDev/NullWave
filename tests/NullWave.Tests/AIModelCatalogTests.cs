using Xunit;
using NullWave.Services.SmartSorting;

namespace NullWave.Tests;

public class AIModelCatalogTests
{
    [Theory]
    // [InlineData(ramGB, vramGB, hasGpu, hasAvx, hasAvx2, isArm64, expectedModel)]
    
    // CPU Path (AVX2 supported)
    [InlineData(4, 0, false, true, true, false, "qwen2.5:0.5b")]
    [InlineData(8, 0, false, true, true, false, "qwen2.5:3b")]
    [InlineData(16, 0, false, true, true, false, "qwen2.5:7b")]
    [InlineData(32, 0, false, true, true, false, "qwen2.5:14b")]
    
    // GPU Path (85% VRAM budget)
    [InlineData(16, 4, true, true, true, false, "qwen2.5:3b")]
    [InlineData(16, 8, true, true, true, false, "qwen2.5:7b")]
    [InlineData(16, 12, true, true, true, false, "qwen2.5:14b")]
    
    // No AVX / Legacy CPU (Should return null / not recommended)
    [InlineData(16, 0, false, false, false, false, null)]
    public void Recommend_MatchesAgreedMatrix(long ramGB, long vramGB, bool hasGpu, bool hasAvx, bool hasAvx2, bool isArm64, string? expectedModel)
    {
        var (model, _) = AIModelCatalog.Recommend(ramGB, vramGB, hasGpu, hasAvx, hasAvx2, isArm64);
        Assert.Equal(expectedModel, model);
    }

    [Fact]
    public void Recommend_FallsBackToCpuPath_WhenGpuBudgetFails()
    {
        // 32GB RAM, but only 2GB VRAM. 
        // GPU path fails the 85% budget for larger models, so it should fall back 
        // to the CPU path and recommend 14B (since 32GB RAM allows Large tier).
        var (model, _) = AIModelCatalog.Recommend(32, 2, true, true, true, false);
        Assert.Equal("qwen2.5:14b", model);
    }

    [Fact]
    public void AllCatalogModels_HaveValidOllamaIds()
    {
        foreach (var model in AIModelCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(model.OllamaId));
            // Ollama IDs can be just the name (defaults to :latest) or name:tag
            // e.g., "phi4-mini" is valid, "qwen2.5:7b" is valid.
            Assert.Matches(@"^[a-zA-Z0-9._-]+(:[a-zA-Z0-9._-]+)?$", model.OllamaId);
        }
    }
}