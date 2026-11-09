namespace NullWave.Plugins.Contracts;

public static class ProviderKindExtensions
{
    /// <summary>True if only one provider of this kind can be active at a time.</summary>
    public static bool IsSingleActive(this ProviderKind kind) => kind switch
    {
        ProviderKind.Download => true,
        ProviderKind.Metadata => true,
        ProviderKind.Mood => true,
        ProviderKind.Weather => true,
        _ => false,
    };
}