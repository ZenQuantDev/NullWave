using System;
using System.IO;
using System.Runtime.CompilerServices;
using NullWave.Helpers;
using Xunit;

namespace NullWave.Tests;

/// <summary>
/// Runs once when the test assembly loads, before any test. Points NullWave's data folder at a
/// throwaway temp folder so no test can ever touch the real library, backups or keys.
/// Requires the NULLWAVE_HOME override in NullWavePaths.cs.
/// </summary>
internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void Init()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nullwave-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("NULLWAVE_HOME", dir);

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        };
    }
}

public class TestEnvironmentGuardTests
{
    [Fact]
    public void Data_folder_is_a_temp_folder_during_tests()
        => Assert.StartsWith(Path.GetTempPath(), NullWavePaths.DataDir, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Legacy_data_folder_is_also_redirected_during_tests()
        => Assert.StartsWith(Path.GetTempPath(), NullWavePaths.LegacyDataDir, StringComparison.OrdinalIgnoreCase);
}