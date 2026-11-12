using System;

namespace NullWave.Plugins.Contracts;

public sealed class PluginLoadException : Exception
{
    public string PluginId { get; }
    public PluginLoadFailureReason Reason { get; }

    public PluginLoadException(string pluginId, PluginLoadFailureReason reason)
        : base($"Plugin '{pluginId}' failed to load: {reason}.")
    {
        PluginId = pluginId;
        Reason = reason;
    }

    public PluginLoadException(string pluginId, PluginLoadFailureReason reason, string message)
        : base(message)
    {
        PluginId = pluginId;
        Reason = reason;
    }

    public PluginLoadException(string pluginId, PluginLoadFailureReason reason, string message, Exception innerException)
        : base(message, innerException)
    {
        PluginId = pluginId;
        Reason = reason;
    }
}

public enum PluginLoadFailureReason
{
    ManifestInvalid,
    EntryPointNotFound,
    InitializationFailed,
    DependencyMissing
}
