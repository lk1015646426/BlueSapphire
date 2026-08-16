namespace BlueSapphire.Tests;

public sealed class UiShellContractTests
{
    [Fact]
    public void MainWindow_UsesSystemBackdropWithThemeBrushFallback()
    {
        string source = File.ReadAllText(Path.Combine(FindProjectRoot(), "MainWindow.xaml.cs"));

        Assert.Contains("TryApplySystemBackdrop", source, StringComparison.Ordinal);
        Assert.Contains("SystemBackdrop = new MicaBackdrop", source, StringComparison.Ordinal);
        Assert.Contains("RootLayout.Background = GetResourceBrush(\"BgColor\"", source, StringComparison.Ordinal);
        Assert.Contains("catch", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellAndSettings_RemoveLegacyAmbientInfrastructureAndPersistReducedMotion()
    {
        string projectRoot = FindProjectRoot();
        Assert.False(File.Exists(Path.Combine(projectRoot, "Part" + "icle.cs")));

        string[] sourceFiles = Directory
            .EnumerateFiles(projectRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}BlueSapphire.Tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (string path in sourceFiles)
        {
            string source = File.ReadAllText(path);
            Assert.DoesNotContain("Toggle" + "Part" + "icleMessage", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IsPart" + "icleEffectEnabled", source, StringComparison.Ordinal);
        }

        string settingsSource = File.ReadAllText(Path.Combine(projectRoot, "Views", "SettingsPage.xaml.cs"));
        Assert.Contains("AppSettings.Save(\"ReduceMotion\", reduceMotion)", settingsSource, StringComparison.Ordinal);
        Assert.Contains("new ToggleReducedMotionMessage(reduceMotion)", settingsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_ClosesWithoutSynchronousWaitAndRemovesSubscriptions()
    {
        string source = File.ReadAllText(Path.Combine(FindProjectRoot(), "MainWindow.xaml.cs"));

        Assert.Contains("Closed += MainWindow_Closed", source, StringComparison.Ordinal);
        Assert.Contains("Closed -= MainWindow_Closed", source, StringComparison.Ordinal);
        Assert.Contains("WeakReferenceMessenger.Default.UnregisterAll(this)", source, StringComparison.Ordinal);
        Assert.Contains("SetWindowMinSize(840, 600)", source, StringComparison.Ordinal);
        string shellXaml = File.ReadAllText(Path.Combine(FindProjectRoot(), "MainWindow.xaml"));
        Assert.Contains("HorizontalContentAlignment=\"Stretch\"", shellXaml, StringComparison.Ordinal);
        Assert.Contains("VerticalContentAlignment=\"Stretch\"", shellXaml, StringComparison.Ordinal);
        Assert.DoesNotContain(".Wait(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAwaiter().GetResult", source, StringComparison.Ordinal);
    }


    [Fact]
    public void LaunchArguments_FallBackToProcessCommandLineAndSupportUtilityPages()
    {
        string root = FindProjectRoot();
        string appSource = File.ReadAllText(Path.Combine(root, "App.xaml.cs"));
        string shellSource = File.ReadAllText(Path.Combine(root, "MainWindow.xaml.cs"));

        Assert.Contains("Environment.GetCommandLineArgs().Skip(1)", appSource, StringComparison.Ordinal);
        Assert.Contains("requestedToolId, \"Settings\"", shellSource, StringComparison.Ordinal);
        Assert.Contains("ContentFrame.Navigate(typeof(SettingsPage))", shellSource, StringComparison.Ordinal);
        Assert.Contains("requestedToolId, \"DevLog\"", shellSource, StringComparison.Ordinal);
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
