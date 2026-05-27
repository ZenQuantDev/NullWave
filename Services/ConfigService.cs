using System;

namespace NullWave.Services;

public class ConfigService
{
    public string GetYouTubeApiKey()
        => Environment.GetEnvironmentVariable("NULLWAVE_YOUTUBE_KEY") ?? string.Empty;
}