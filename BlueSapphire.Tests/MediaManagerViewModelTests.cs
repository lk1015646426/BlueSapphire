using BlueSapphire.ViewModels;
using System.Reflection;

namespace BlueSapphire.Tests;

public class MediaManagerViewModelTests
{
    [Fact]
    public void NormalizeFolderPathInput_ReturnsNull_ForEmptyInput()
    {
        Assert.Null(InvokeNormalizeFolderPathInput("   "));
    }

    [Fact]
    public void NormalizeFolderPathInput_ExpandsEnvironmentVariables_AndTrimsQuotes()
    {
        string expected = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        string input = "\"%LOCALAPPDATA%\"";

        string? actual = InvokeNormalizeFolderPathInput(input);

        Assert.Equal(expected, actual);
    }

    private static string? InvokeNormalizeFolderPathInput(string? input)
    {
        MethodInfo method = typeof(MediaManagerViewModel).GetMethod(
            "NormalizeFolderPathInput",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        return (string?)method.Invoke(null, [input]);
    }
}
