using System;
using System.Text.Json.Serialization;

namespace NullWave.Helpers;

public enum BadgeKind { Granted, Computed }
public enum BadgeCategory { Milestone, Community, Special }

public record BadgeRule
{
    [JsonPropertyName("metric")]
    public string Metric { get; init; } = "";
    
    [JsonPropertyName("op")]
    public string Op { get; init; } = "";
    
    [JsonPropertyName("value")]
    public object? Value { get; init; }
}

public record BadgeDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
    
    [JsonPropertyName("kind")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BadgeKind Kind { get; init; }
    
    [JsonPropertyName("category")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BadgeCategory Category { get; init; }
    
    [JsonPropertyName("icon")]
    public string Icon { get; init; } = "";
    
    [JsonPropertyName("color")]
    public string Color { get; init; } = "";
    
    [JsonPropertyName("nameKey")]
    public string NameKey { get; init; } = "";
    
    [JsonPropertyName("descKey")]
    public string DescKey { get; init; } = "";
    
    [JsonPropertyName("rule")]
    public BadgeRule? Rule { get; init; }
}
