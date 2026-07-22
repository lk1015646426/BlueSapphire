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
