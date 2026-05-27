using System;
using System.IO;
using System.Security.Cryptography;
using Serilog;

namespace NullWave.Services;

public class SecureDeleteService
{
    private readonly KeyStoreService _keyStore;
    private readonly string _nullwaveDir;

    public SecureDeleteService(KeyStoreService keyStore)
    {
        _keyStore = keyStore;
        _nullwaveDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nullwave");
    }

    // Wipe only API keys
    public void DeleteApiKeys()
    {
        _keyStore.DeleteAllKeys();
    }

    // Wipe logs only
    public void DeleteLogs()
    {
        var logDir = Path.Combine(_nullwaveDir, "logs");
        if (!Directory.Exists(logDir)) return;

        foreach (var file in Directory.GetFiles(logDir, "*.log"))
            SecureWipeFile(file);

        Log.Warning("All logs have been securely deleted");
    }

    // Nuclear option — wipe everything NullWave has stored
    public void DeleteEverything()
    {
        DeleteApiKeys();
        DeleteLogs();

        // Wipe any other files in ~/.nullwave
        if (Directory.Exists(_nullwaveDir))
        {
            foreach (var file in Directory.GetFiles(_nullwaveDir))
                SecureWipeFile(file);
        }

        Log.Warning("NullWave data fully wiped");
    }

    private static void SecureWipeFile(string path)
    {
        try
        {
            var size = new FileInfo(path).Length;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write))
            {
                // Three-pass wipe
                for (int i = 0; i < 3; i++)
                {
                    fs.Seek(0, SeekOrigin.Begin);
                    var noise = RandomNumberGenerator.GetBytes((int)size);
                    fs.Write(noise, 0, noise.Length);
                    fs.Flush();
                }
            }
            File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Secure wipe failed for {Path}", path);
        }
    }
}