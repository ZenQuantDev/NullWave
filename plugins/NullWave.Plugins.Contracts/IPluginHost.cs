using System.Threading;
using System.Threading.Tasks;

namespace NullWave.Plugins.Contracts;

public interface IPluginHost
{
    string GetApiKey(string keyName);
    void ShowToast(string message, ToastType type, int durationMs = 3000);
    Task<string> DownloadToTempAsync(string url, CancellationToken ct);
    void Log(LogLevel level, string message);
}
