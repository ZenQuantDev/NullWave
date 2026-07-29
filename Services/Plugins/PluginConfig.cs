using System.Collections.Generic;

namespace NullWave.Models;

/// <summary>
/// Per-plugin configuration stored inside <see cref="Preferences"/>.
/// </summary>
public class PluginConfig
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 0;
    public Dictionary<string, string> Settings { get; set; } = new();
}