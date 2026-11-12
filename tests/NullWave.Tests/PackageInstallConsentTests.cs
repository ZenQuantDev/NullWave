using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Services;
using Xunit;

namespace NullWave.Tests;

public sealed class PackageInstallConsentTests
{
    [Fact]
    public async Task Declined_package_install_does_not_start_a_process()
    {
        var processStarts = 0;
        string? commandShown = null;
        var service = new DependencyUpdateService((_, _, _) =>
        {
            processStarts++;
            return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, false, false));
        });

        var result = await service.RunPackageInstallAsync(
            "winget",
            new List<string> { "install", "--id", "VideoLAN.VLC", "-e" },
            command =>
            {
                commandShown = command;
                return Task.FromResult(false);
            });

        Assert.False(result.Approved);
        Assert.Null(result.ProcessResult);
        Assert.Equal(0, processStarts);
        Assert.Equal("winget install --id VideoLAN.VLC -e", commandShown);
    }

    [Fact]
    public async Task Approved_package_install_runs_the_exact_command_after_consent()
    {
        string? executable = null;
        IList<string>? arguments = null;
        var service = new DependencyUpdateService((exe, args, _) =>
        {
            executable = exe;
            arguments = args;
            return Task.FromResult(new ProcessResult(0, "installed", string.Empty, false, false));
        });

        var result = await service.RunPackageInstallAsync(
            "winget",
            new List<string> { "install", "--id", "VideoLAN.VLC", "-e" },
            command => Task.FromResult(command == "winget install --id VideoLAN.VLC -e"));

        Assert.True(result.Approved);
        Assert.Equal("winget", executable);
        Assert.Equal(new[] { "install", "--id", "VideoLAN.VLC", "-e" }, arguments);
        Assert.Equal("installed", result.ProcessResult?.StandardOutput);
    }
}
