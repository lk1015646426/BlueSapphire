using System.Text.RegularExpressions;

namespace BlueSapphire.Tests;

public sealed class UiThemeContractTests
{
    private static readonly string[] RequiredThemeKeys =
    [
        "BgColor",
        "SidebarBackground",
        "PanelSurface",
        "TextMain",
        "TextSecondary",
        "AccentPrimary",
        "AccentPrimaryHover",
        "AccentPrimaryPressed",
        "AccentPrimaryBg",
        "AccentSafe",
        "AccentReview",
        "AccentInspect",
        "AccentDanger",
        "PrimaryActionButtonStyle",
        "CompactButtonStyle",
        "TextStyle_MetricLarge"
    ];

    [Fact]
    public void SharedTheme_ExposesRequiredKeysExactlyOnce()
    {
        string source = File.ReadAllText(Path.Combine(FindProjectRoot(), "Themes", "SharedTheme.xaml"));
        MatchCollection matches = Regex.Matches(source, "x:Key\\s*=\\s*[\"']([^\"']+)");
        string[] keys = matches.Select(match => match.Groups[1].Value).ToArray();

        string[] duplicateKeys = keys
            .GroupBy(key => key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(duplicateKeys);
        foreach (string requiredKey in RequiredThemeKeys)
        {
            Assert.Contains(requiredKey, keys);
        }
    }

    [Fact]
    public void AppThemePalette_DefinesEightPresetsAndFourFontSizes()
    {
        string source = File.ReadAllText(Path.Combine(FindProjectRoot(), "App.xaml.cs"));
        string[] presets = ["default", "sky", "cobalt", "graphite", "lagoon", "ink", "ochre", "sepia"];

        foreach (string preset in presets)
        {
            Assert.Contains($"(\"{preset}\", false)", source, StringComparison.Ordinal);
            if (preset != "default")
            {
                Assert.Contains($"(\"{preset}\", true)", source, StringComparison.Ordinal);
            }
        }

        Assert.Contains("\"small\" => 13", source, StringComparison.Ordinal);
        Assert.Contains("_ => 14", source, StringComparison.Ordinal);
        Assert.Contains("\"medium\" => 15", source, StringComparison.Ordinal);
        Assert.Contains("\"large\" => 16", source, StringComparison.Ordinal);
        Assert.Contains("Get(\"UiFontSize\", \"standard\")", source, StringComparison.Ordinal);
    }

    private static string FindProjectRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BlueSapphire.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("未找到 BlueSapphire 项目根目录。");
    }
}
