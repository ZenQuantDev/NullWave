using System;

namespace NullWave.Models;

public class DownloaderConsent
{
    public const int CurrentVersion = 1;
    
    public int AcceptedVersion { get; set; }
    public DateTime AcceptedAtUtc { get; set; }
    
    public bool IsCurrent => AcceptedVersion >= CurrentVersion;
}