using System;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Plugins.Contracts;
using NullWave.Services;
using NullWave.Services.Plugins;
using NullWave.Services.Security;
using Xunit;

namespace NullWave.Tests;

public class PluginHostTests
{
    private static PluginManifest CreateTestManifest(bool accessApiKeys = false, bool executeProcesses = false)
    {
        return new PluginManifest(
            Id: "test.plugin",
            Name: "Test Plugin",
            Author: "Test Author",
            Version: "1.0.0",
            Description: "Test description",
            Kinds: new[] { ProviderKind.Download },
            BuiltIn: false,
            Availability: PluginAvailability.Installed)
        {
            Permissions = new PluginPermissions
            {
                AccessApiKeys = accessApiKeys,
                ApiKeys = accessApiKeys ? new[] { "TestKey" } : Array.Empty<string>(),
                ExecuteProcesses = executeProcesses
            }
        };
    }

    [Fact]
    public void Constructor_SetsProperties()
    {
        var prefs = new PreferencesService();
        var keyStore = new KeyStoreService(Path.Combine(Path.GetTempPath(), "test-keys.enc"));
        var manifest = CreateTestManifest();

        var host = new PluginHost(prefs, keyStore, manifest);

        Assert.Equal(AppVersionInfo.Current, host.HostVersion);
        Assert.Same(manifest, host.Manifest);
    }

    [Fact]
    public void GetApiKey_WithoutPermission_ReturnsNull()
    {
        var prefs = new PreferencesService();
        var keyStore = new KeyStoreService(Path.Combine(Path.GetTempPath(), "test-keys.enc"));
        var manifest = CreateTestManifest(accessApiKeys: false);
        var host = new PluginHost(prefs, keyStore, manifest);

        keyStore.SaveKey("TestKey", "test-value");

        var result = host.GetApiKey("TestKey");

        Assert.Null(result);
    }

    [Fact]
    public void GetApiKey_WithPermission_ReturnsKey()
    {
        var prefs = new PreferencesService();
        var keyStore = new KeyStoreService(Path.Combine(Path.GetTempPath(), "test-keys.enc"));
        var manifest = CreateTestManifest(accessApiKeys: true);
        var host = new PluginHost(prefs, keyStore, manifest);

        keyStore.SaveKey("TestKey", "test-value");

        var result = host.GetApiKey("TestKey");

        Assert.Equal("test-value", result);
    }

    [Fact]
    public void GetApiKey_refuses_undeclared_key_even_with_permission()
    {
        var prefs = new PreferencesService();
        var keyStore = new KeyStoreService(Path.Combine(Path.GetTempPath(), $"nw-undeclared-{Guid.NewGuid():N}.enc"));
        var host = new PluginHost(prefs, keyStore, CreateTestManifest(accessApiKeys: true));
        keyStore.SaveKey("LastFm:SessionKey", "secret");

        Assert.Null(host.GetApiKey("LastFm:SessionKey"));
    }

    [Fact]
    public void GetSetting_ThrowsNotImplemented()
    {
        var prefs = new PreferencesService();
        var keyStore = new KeyStoreService(Path.Combine(Path.GetTempPath(), "test-keys.enc"));
        var manifest = CreateTestManifest();
        var host = new PluginHost(prefs, keyStore, manifest);

        Assert.Throws<NotImplementedException>(() => host.GetSetting<string>("test-key"));
    }

    [Fact]
    public void SetSetting_ThrowsNotImplemented()
    {
        var prefs = new PreferencesService();
        var keyStore = new KeyStoreService(Path.Combine(Path.GetTempPath(), "test-keys.enc"));
        var manifest = CreateTestManifest();
        var host = new PluginHost(prefs, keyStore, manifest);

        Assert.Throws<NotImplementedException>(() => host.SetSetting("test-key", "test-value"));
    }

    [Fact]
    public async Task ConfirmAsync_ReturnsFalse()
    {
        var prefs = new PreferencesService();
        var keyStore = new KeyStoreService(Path.Combine(Path.GetTempPath(), "test-keys.enc"));
        var manifest = CreateTestManifest();
        var host = new PluginHost(prefs, keyStore, manifest);

        var result = await host.ConfirmAsync("Test Title", "Test Message");

        Assert.False(result);
    }

    [Fact]
    public void GetLibraryTracks_ThrowsNotImplemented()
    {
        var prefs = new PreferencesService();
        var keyStore = new KeyStoreService(Path.Combine(Path.GetTempPath(), "test-keys.enc"));
        var manifest = CreateTestManifest();
        var host = new PluginHost(prefs, keyStore, manifest);

        Assert.Throws<NotImplementedException>(() => host.GetLibraryTracks());
    }

    [Fact]
    public async Task DownloadToTempAsync_ThrowsNotImplemented()
    {
        var prefs = new PreferencesService();
        var keyStore = new KeyStoreService(Path.Combine(Path.GetTempPath(), "test-keys.enc"));
        var manifest = CreateTestManifest();
        var host = new PluginHost(prefs, keyStore, manifest);

        await Assert.ThrowsAsync<NotImplementedException>(() => host.DownloadToTempAsync("https://example.com/file"));
    }

    [Fact]
    public async Task RunProcessAsync_WithoutPermission_ReturnsDenied()
    {
        var prefs = new PreferencesService();
        var keyStore = new KeyStoreService(Path.Combine(Path.GetTempPath(), "test-keys.enc"));
        var manifest = CreateTestManifest(executeProcesses: false);
        var host = new PluginHost(prefs, keyStore, manifest);

        var result = await host.RunProcessAsync("test.exe", Array.Empty<string>(), TimeSpan.FromSeconds(5));

        Assert.Equal(-1, result.ExitCode);
        Assert.Equal("Permission denied", result.StandardError);
    }

    [Fact]
    public async Task RunProcessAsync_WithPermission_ThrowsNotImplemented()
    {
        var prefs = new PreferencesService();
        var keyStore = new KeyStoreService(Path.Combine(Path.GetTempPath(), "test-keys.enc"));
        var manifest = CreateTestManifest(executeProcesses: true);
        var host = new PluginHost(prefs, keyStore, manifest);

        await Assert.ThrowsAsync<NotImplementedException>(() => 
            host.RunProcessAsync("test.exe", Array.Empty<string>(), TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ShowToast_CallsToastService()
    {
        var prefs = new PreferencesService();
        var keyStore = new KeyStoreService(Path.Combine(Path.GetTempPath(), "test-keys.enc"));
        var manifest = CreateTestManifest();
        var host = new PluginHost(prefs, keyStore, manifest);

        // Should not throw
        host.ShowToast("Test message", "Info", 3000);
    }

    [Fact]
    public void Log_RoutesToSerilog()
    {
        var prefs = new PreferencesService();
        var keyStore = new KeyStoreService(Path.Combine(Path.GetTempPath(), "test-keys.enc"));
        var manifest = CreateTestManifest();
        var host = new PluginHost(prefs, keyStore, manifest);

        // Should not throw
        host.Log(LogLevel.Info, "Test message");
        host.Log(LogLevel.Warning, "Warning message");
        host.Log(LogLevel.Error, "Error message", new Exception("Test exception"));
    }
}