using NullWave.Helpers;

namespace NullWave.Tests.Helpers;

/// <summary>
/// Uses real temporary folders with tiny fake "DLLs" that carry a valid Windows header, so the
/// file-system and bitness checks run for real. No VLC needs to be installed.
/// </summary>
public class VlcLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nw-vlc-" + Guid.NewGuid().ToString("N"));

    public VlcLocatorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    public enum Plugins { Present, Empty, Missing }

    private static byte[] FakePe(ushort machine)
    {
        var bytes = new byte[0x100];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3C);      // e_lfanew: where the PE header starts
        bytes[0x80] = (byte)'P';
        bytes[0x81] = (byte)'E';
        BitConverter.GetBytes(machine).CopyTo(bytes, 0x84);   // Machine field
        return bytes;
    }

    private string MakeInstall(string name, ushort machine = VlcLocator.MachineX64,
        bool core = true, Plugins plugins = Plugins.Present)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "libvlc.dll"), FakePe(machine));
        if (core) File.WriteAllBytes(Path.Combine(dir, "libvlccore.dll"), FakePe(machine));

        if (plugins != Plugins.Missing)
        {
            var pluginDir = Path.Combine(dir, "plugins", "audio_output");
            Directory.CreateDirectory(pluginDir);
            if (plugins == Plugins.Present)
                File.WriteAllBytes(Path.Combine(pluginDir, "libwasapi_plugin.dll"), new byte[] { 1 });
        }

        return dir;
    }

    private static VlcCandidate Candidate(string dir, VlcSource source = VlcSource.Registry) => new(dir, source);

    [Fact]
    public void A_complete_install_that_matches_the_process_is_usable()
    {
        var dir = MakeInstall("ok");

        var result = VlcLocator.Evaluate(new[] { Candidate(dir) }, VlcLocator.MachineX64);

        Assert.Equal(dir, result.Directory);
        Assert.True(result.Checks.Single().Usable);
        Assert.Null(result.Checks.Single().Reason);
    }

    [Fact]
    public void A_missing_core_library_is_rejected_with_a_reason()
    {
        var dir = MakeInstall("nocore", core: false);

        var result = VlcLocator.Evaluate(new[] { Candidate(dir) }, VlcLocator.MachineX64);

        Assert.Null(result.Directory);
        Assert.Contains("libvlccore.dll", result.Checks.Single().Reason);
    }

    [Theory]
    [InlineData(Plugins.Missing)]
    [InlineData(Plugins.Empty)]
    public void A_missing_or_empty_plugins_folder_is_rejected(Plugins plugins)
    {
        var dir = MakeInstall("noplugins", plugins: plugins);

        var result = VlcLocator.Evaluate(new[] { Candidate(dir) }, VlcLocator.MachineX64);

        Assert.Null(result.Directory);
        Assert.Contains("plugins", result.Checks.Single().Reason);
    }

    [Fact]
    public void A_32_bit_install_is_rejected_for_a_64_bit_process()
    {
        var dir = MakeInstall("x86", machine: VlcLocator.MachineX86);

        var result = VlcLocator.Evaluate(new[] { Candidate(dir) }, VlcLocator.MachineX64);

        Assert.Null(result.Directory);
        var reason = result.Checks.Single().Reason;
        Assert.Contains("x86", reason);
        Assert.Contains("x64", reason);
    }

    [Fact]
    public void An_unknown_process_architecture_skips_the_bitness_check()
    {
        var dir = MakeInstall("x86", machine: VlcLocator.MachineX86);

        var result = VlcLocator.Evaluate(new[] { Candidate(dir) }, processMachine: null);

        Assert.Equal(dir, result.Directory);
    }

    [Fact]
    public void A_file_that_is_not_a_windows_library_is_rejected()
    {
        var dir = MakeInstall("fake");
        File.WriteAllText(Path.Combine(dir, "libvlc.dll"), "this is not a dll");

        var result = VlcLocator.Evaluate(new[] { Candidate(dir) }, VlcLocator.MachineX64);

        Assert.Null(result.Directory);
        Assert.Contains("not a valid", result.Checks.Single().Reason);
    }

    [Fact]
    public void A_folder_that_does_not_exist_is_rejected()
    {
        var result = VlcLocator.Evaluate(new[] { Candidate(Path.Combine(_root, "nope")) }, VlcLocator.MachineX64);

        Assert.Null(result.Directory);
        Assert.Contains("does not exist", result.Checks.Single().Reason);
    }

    [Fact]
    public void The_first_usable_candidate_wins_and_later_ones_are_not_checked()
    {
        var broken = MakeInstall("broken", core: false);
        var good = MakeInstall("good");
        var alsoGood = MakeInstall("alsogood");

        var result = VlcLocator.Evaluate(new[] { Candidate(broken), Candidate(good), Candidate(alsoGood) }, VlcLocator.MachineX64);

        Assert.Equal(good, result.Directory);
        Assert.Equal(2, result.Checks.Count);          // alsoGood was never looked at
        Assert.False(result.Checks[0].Usable);
        Assert.True(result.Checks[1].Usable);
    }

    [Fact]
    public void When_nothing_is_usable_every_reason_is_reported()
    {
        var a = MakeInstall("a", core: false);
        var b = MakeInstall("b", plugins: Plugins.Missing);

        var result = VlcLocator.Evaluate(new[] { Candidate(a), Candidate(b) }, VlcLocator.MachineX64);

        Assert.Null(result.Directory);
        Assert.Equal(2, result.Checks.Count);
        Assert.All(result.Checks, c => Assert.False(string.IsNullOrWhiteSpace(c.Reason)));
    }

    [Fact]
    public void Candidates_are_ordered_override_registry_program_files_bundled_without_duplicates()
    {
        var a = Path.Combine(_root, "a");
        var b = Path.Combine(_root, "b");
        var c = Path.Combine(_root, "c");
        var d = Path.Combine(_root, "d");

        var list = VlcLocator.BuildCandidates(
            overrideDir: "  \"" + a + "\"  ",                       // quotes and spaces are tolerated
            registryDirs: new[] { b, b + Path.DirectorySeparatorChar, a },   // duplicates of b and of the override
            programFilesDirs: new[] { c, "" },                       // blank entries are skipped
            bundledDir: d);

        Assert.Equal(new[] { a, b, c, d }, list.Select(x => x.Directory));
        Assert.Equal(
            new[] { VlcSource.Override, VlcSource.Registry, VlcSource.ProgramFiles, VlcSource.Bundled },
            list.Select(x => x.Source));
    }

    [Fact]
    public void Missing_inputs_give_an_empty_candidate_list()
    {
        var list = VlcLocator.BuildCandidates(null, Array.Empty<string>(), Array.Empty<string>(), null);

        Assert.Empty(list);
    }

    [Theory]
    [InlineData(VlcLocator.MachineX64)]
    [InlineData(VlcLocator.MachineX86)]
    [InlineData(VlcLocator.MachineArm64)]
    public void ReadMachine_reads_the_cpu_type_from_the_header(ushort machine)
    {
        var path = Path.Combine(_root, "lib.dll");
        File.WriteAllBytes(path, FakePe(machine));

        Assert.Equal(machine, VlcLocator.ReadMachine(path));
    }

    [Fact]
    public void ReadMachine_returns_null_for_garbage_truncated_and_missing_files()
    {
        var text = Path.Combine(_root, "text.dll");
        File.WriteAllText(text, "hello");

        var truncated = Path.Combine(_root, "short.dll");
        File.WriteAllBytes(truncated, new byte[] { (byte)'M', (byte)'Z', 0, 0 });

        var badOffset = Path.Combine(_root, "offset.dll");
        var bytes = FakePe(VlcLocator.MachineX64);
        BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, 0x3C);
        File.WriteAllBytes(badOffset, bytes);

        Assert.Null(VlcLocator.ReadMachine(text));
        Assert.Null(VlcLocator.ReadMachine(truncated));
        Assert.Null(VlcLocator.ReadMachine(badOffset));
        Assert.Null(VlcLocator.ReadMachine(Path.Combine(_root, "missing.dll")));
    }
}