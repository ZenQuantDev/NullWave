namespace NullWave.Models;

/// <summary>
/// Defines the visual effects capability tier for the current session.
/// </summary>
public enum EffectsTier
{
    Minimal,   // No shaders, no glow, static dimming off, reduced/no animation, True Black forced on OLED/Battery
    Standard,  // Current default behavior — everything already shipped in v0.6.2
    Full       // Reserved for future GPU-heavy features (ambient glow, visualizer, parallax)
}