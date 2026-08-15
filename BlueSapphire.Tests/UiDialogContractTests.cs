using System.Text.RegularExpressions;

namespace BlueSapphire.Tests;

public sealed class UiDialogContractTests
{
    private static readonly string[] DialogFiles =
    [
        "DuplicateResultDialog.xaml",
        "RenamePreviewDialog.xaml",
        Path.Combine("Views", "Dialogs", "AdvancedImageEditorDialog.xaml"),
        Path.Combine("Views", "Dialogs", "EnhanceImageDialog.xaml"),
        Path.Combine("Views", "Dialogs", "FormatConvertDialog.xaml"),
        Path.Combine("Views", "SkillProxyConfigDialog.xaml")
    ];

    [Fact]
    public void Dialogs_ResolveSharedResourcesAndUseWorkbenchStyle()
    {
        string root = FindProjectRoot();
        string[] definitionFiles = ["App.xaml", Path.Combine("Themes", "SharedTheme.xaml"), .. DialogFiles];
        HashSet<string> keys = definitionFiles
            .Select(path => File.ReadAllText(Path.Combine(root, path)))
            .SelectMany(source => Regex.Matches(source, "x:Key\\s*=\\s*[\"']([^\"']+)").Select(match => match.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        foreach (string dialogFile in DialogFiles)
        {
            string source = File.ReadAllText(Path.Combine(root, dialogFile));
            Assert.Contains("Style=\"{StaticResource WorkbenchContentDialogStyle}\"", source, StringComparison.Ordinal);

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
    public void OperationDialogs_ExposePrimaryAndCancelActions()
    {
        string root = FindProjectRoot();
        foreach (string dialogFile in DialogFiles.Take(5))
        {
            string source = File.ReadAllText(Path.Combine(root, dialogFile));
            Assert.Matches("PrimaryButtonText=\"[^\"]+\"", source);
            Assert.Contains("CloseButtonText=\"取消\"", source, StringComparison.Ordinal);
            Assert.Contains("DefaultButton=\"Close\"", source, StringComparison.Ordinal);
        }

        string proxyDialog = File.ReadAllText(Path.Combine(root, DialogFiles[5]));
        Assert.Contains("AutomationProperties.Name=\"使用国内直连\"", proxyDialog, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"跟随系统网络设置\"", proxyDialog, StringComparison.Ordinal);
        Assert.Contains("Content=\"取消\"", proxyDialog, StringComparison.Ordinal);
    }

    [Fact]
    public void DestructiveAndLongDialogs_ExplainScopeOrPreservation()
    {
        string root = FindProjectRoot();
        string duplicate = File.ReadAllText(Path.Combine(root, "DuplicateResultDialog.xaml"));
        string editor = File.ReadAllText(Path.Combine(root, "Views", "Dialogs", "AdvancedImageEditorDialog.xaml"));
        string converter = File.ReadAllText(Path.Combine(root, "Views", "Dialogs", "FormatConvertDialog.xaml"));

        Assert.Contains("移至回收站", duplicate, StringComparison.Ordinal);
        Assert.Contains("不会覆盖原文件", editor, StringComparison.Ordinal);
        Assert.Contains("原文件不会被覆盖", converter, StringComparison.Ordinal);
    }


    [Fact]
    public void ResponsiveDialogs_RemoveFixedWidthsAndKeepContentScrollBounded()
    {
        string root = FindProjectRoot();
        Dictionary<string, string[]> forbidden = new(StringComparer.Ordinal)
        {
            [Path.Combine("Views", "Dialogs", "AdvancedImageEditorDialog.xaml")] = ["MinWidth=\"780\"", "Width=\"360\"", "<x:Double x:Key=\"ContentDialogMaxWidth\">1200</x:Double>"],
            ["DuplicateResultDialog.xaml"] = ["Width=\"620\"", "MaxHeight=\"430\""],
            [Path.Combine("Views", "Dialogs", "EnhanceImageDialog.xaml")] = ["Width=\"440\"", "MaxHeight=\"560\"", "Height=\"190\""],
            [Path.Combine("Views", "DevLogInputDialog.xaml")] = ["Width=\"600\"", "MaxHeight=\"580\""]
        };

        foreach ((string dialogFile, string[] blockedFragments) in forbidden)
        {
            string source = File.ReadAllText(Path.Combine(root, dialogFile));

            Assert.Contains("HorizontalAlignment=\"Stretch\"", source, StringComparison.Ordinal);
            Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", source, StringComparison.Ordinal);
            Assert.Contains("VisualStateManager.VisualStateGroups", source, StringComparison.Ordinal);
            Assert.Contains("AdaptiveTrigger", source, StringComparison.Ordinal);

            foreach (string blocked in blockedFragments)
            {
                Assert.DoesNotContain(blocked, source, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void DialogCancelButtonsUseThemedWeakSurfaceInsteadOfAccentOrTransparent()
    {
        string root = FindProjectRoot();
        string theme = File.ReadAllText(Path.Combine(root, "Themes", "SharedTheme.xaml"));

        Match subtleStyle = Regex.Match(
            theme,
            "<Style x:Key=\"SubtleActionButtonStyle\"[\\s\\S]*?</Style>",
            RegexOptions.CultureInvariant);

        Assert.True(subtleStyle.Success, "SubtleActionButtonStyle must be declared.");
        Assert.Contains("PanelSurfaceStrong", subtleStyle.Value, StringComparison.Ordinal);
        Assert.Contains("BorderSubtle", subtleStyle.Value, StringComparison.Ordinal);
        Assert.Contains("PanelHighlight", subtleStyle.Value, StringComparison.Ordinal);
        Assert.Contains("SurfaceElevated", subtleStyle.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("Value=\"Transparent\"", subtleStyle.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("AccentPrimary", subtleStyle.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeThemePresetUpdatesNativeAccentBrushes()
    {
        string root = FindProjectRoot();
        string app = File.ReadAllText(Path.Combine(root, "App.xaml.cs"));
        string theme = File.ReadAllText(Path.Combine(root, "Themes", "SharedTheme.xaml"));

        Assert.Contains("ApplyNativeAccentResources", app, StringComparison.Ordinal);
        Assert.Contains("AccentFillColorDefaultBrush", theme, StringComparison.Ordinal);
        Assert.Contains("AccentTextFillColorPrimaryBrush", theme, StringComparison.Ordinal);
        Assert.Contains("SystemAccentColor", app, StringComparison.Ordinal);
        Assert.Contains("SystemAccentColorLight1", app, StringComparison.Ordinal);
        Assert.Contains("SystemAccentColorDark1", app, StringComparison.Ordinal);
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

