using System.Text.RegularExpressions;

namespace BlueSapphire.Tests;

public sealed class UiPageContractTests
{
    private static readonly string[] PageFiles =
    [
        "MediaManagerPage.xaml",
        Path.Combine("Views", "DevLogPage.xaml"),
        Path.Combine("Views", "DevLogInputDialog.xaml")
    ];

    [Fact]
    public void PrimaryPages_ResolveAllDeclaredResourceReferences()
    {
        string root = FindProjectRoot();
        string[] definitionFiles = ["App.xaml", Path.Combine("Themes", "SharedTheme.xaml"), .. PageFiles];
        HashSet<string> keys = definitionFiles
            .Select(path => File.ReadAllText(Path.Combine(root, path)))
            .SelectMany(source => Regex.Matches(source, "x:Key\\s*=\\s*[\"']([^\"']+)").Select(match => match.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        foreach (string pageFile in PageFiles)
        {
            string source = File.ReadAllText(Path.Combine(root, pageFile));
            string[] references = Regex.Matches(source, "\\{(?:StaticResource|ThemeResource)\\s+([^},\\s]+)")
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            foreach (string reference in references)
            {
                Assert.Contains(reference, keys);
            }
        }
    }

    [Fact]
    public void MediaWorkspace_KeepsSelectedActionsReachableAtNarrowWidths()
    {
        string source = File.ReadAllText(Path.Combine(FindProjectRoot(), "MediaManagerPage.xaml"));

        Assert.Contains("x:Name=\"SelectedActionsScroller\"", source, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", source, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollMode=\"Enabled\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DevLogLayouts_WrapContentAndBoundDialogHeight()
    {
        string root = FindProjectRoot();
        string page = File.ReadAllText(Path.Combine(root, "Views", "DevLogPage.xaml"));
        string dialog = File.ReadAllText(Path.Combine(root, "Views", "DevLogInputDialog.xaml"));

        Assert.Contains("TextWrapping=\"Wrap\"", page, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"1100\"", page, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"580\"", dialog, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", dialog, StringComparison.Ordinal);
    }


    [Fact]
    public void HomeWorkspace_HidesSecondaryActivityPanelAtNarrowWidths()
    {
        string source = File.ReadAllText(Path.Combine(FindProjectRoot(), "HomePage.xaml"));

        Assert.Contains("x:Name=\"RecentActivityCard\"", source, StringComparison.Ordinal);
        Assert.Contains("MinWindowWidth=\"1000\"", source, StringComparison.Ordinal);
        Assert.Contains("Target=\"RecentActivityCard.Visibility\" Value=\"Collapsed\"", source, StringComparison.Ordinal);
    }


    [Fact]
    public void CleanerWorkspace_UsesNarrowLayoutAndAppliesReducedMotionImmediately()
    {
        string root = FindProjectRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "CleanerAssistantPage.xaml"));
        string code = File.ReadAllText(Path.Combine(root, "CleanerAssistantPage.xaml.cs"));

        Assert.Contains("x:Name=\"ScanDetailPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"ScanDetailPanel.Visibility\" Value=\"Collapsed\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"SectionRailColumn.Width\" Value=\"0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"MainTabBar.Visibility\" Value=\"Collapsed\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"InlineQuickScanButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Register<ToggleReducedMotionMessage>", code, StringComparison.Ordinal);
        Assert.Contains("Unregister<ToggleReducedMotionMessage>", code, StringComparison.Ordinal);
    }


    [Fact]
    public void NarrowShell_CollapsesSecondaryCopilotToolsAndUsesTwoColumnThemeGrid()
    {
        string root = FindProjectRoot();
        string copilot = File.ReadAllText(Path.Combine(root, "Views", "AICopilotPage.xaml"));
        string settings = File.ReadAllText(Path.Combine(root, "SettingsPage.xaml"));

        Assert.Contains("Target=\"HeaderTools.Visibility\" Value=\"Collapsed\"", copilot, StringComparison.Ordinal);
        Assert.Contains("Target=\"ConnectionPill.Visibility\" Value=\"Collapsed\"", copilot, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ThemePresetGrid\"", settings, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"Auto\"/><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"Auto\"/>", settings, StringComparison.Ordinal);
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
