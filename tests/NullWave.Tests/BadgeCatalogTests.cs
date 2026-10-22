using System;
using System.IO;
using Material.Icons;
using NullWave.Helpers;
using Xunit;

namespace NullWave.Tests;

public class BadgeCatalogTests
{
    public BadgeCatalogTests() => BadgeCatalog.Load();

    private static string RepoFile(string relative)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "NullWave.csproj")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return Path.Combine(dir!, relative);
    }

    [Fact]
    public void Catalog_loads() => Assert.True(BadgeCatalog.Definitions.Count >= 10);

    [Fact]
    public void Every_icon_is_a_real_MaterialIconKind()
    {
        foreach (var d in BadgeCatalog.Definitions.Values)
            Assert.True(Enum.TryParse<MaterialIconKind>(d.Icon, out _), $"{d.Id}: unknown icon '{d.Icon}'");
    }

    [Fact]
    public void Computed_badges_have_rules_and_granted_badges_do_not()
    {
        foreach (var d in BadgeCatalog.Definitions.Values)
            Assert.Equal(d.Kind == BadgeKind.Computed, d.Rule != null);
    }

    [Fact]
    public void Every_color_token_exists_in_Colors_axaml()
    {
        var xaml = File.ReadAllText(RepoFile(Path.Combine("Themes", "Colors.axaml")));
        foreach (var d in BadgeCatalog.Definitions.Values)
            Assert.True(xaml.Contains("x:Key=\"" + d.Color + "\""), $"{d.Id}: color '{d.Color}' not defined");
    }
}
