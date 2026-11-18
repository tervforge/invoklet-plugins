using System.Text.Json;
using Invoklet.Avalonia.Themes;
using Xunit;

namespace Invoklet.Themes.Tests;

// Every palette a theme package lists defines exactly the palette keys Invoklet has, so a theme leaves no control unstyled.
public sealed class ThemePackageTests
{
    public static TheoryData<string> Palettes()
    {
        var data = new TheoryData<string>();
        foreach (var manifest in Directory.GetFiles(Path.Combine(RepositoryRoot(), "themes"), "invoklet-plugin.json", SearchOption.AllDirectories))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifest));
            foreach (var palette in document.RootElement.GetProperty("assets").GetProperty("themes").EnumerateArray())
                data.Add(Path.GetRelativePath(RepositoryRoot(), Path.Combine(Path.GetDirectoryName(manifest)!, palette.GetString()!)));
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void Palette_LoadedFromJson_DefinesEveryCataloguedPaletteKey(string palette)
    {
        var result = new ThemeDefinitionReader().Read(File.ReadAllText(Path.Combine(RepositoryRoot(), palette)));

        Assert.Empty(result.Diagnostics);
        var definition = Assert.IsType<ThemeDefinition>(result.Definition);
        Assert.Equal(ThemeKeyCatalog.Palette.Select(key => key.Name).Order(StringComparer.Ordinal),
            definition.Resources.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Palettes_OfAllPackages_HaveDistinctIds()
    {
        var ids = Palettes().Select(row => new ThemeDefinitionReader().Read(File.ReadAllText(Path.Combine(RepositoryRoot(), row.Data))).Definition!.Id).ToList();

        Assert.Equal(6, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PluginPackages.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
