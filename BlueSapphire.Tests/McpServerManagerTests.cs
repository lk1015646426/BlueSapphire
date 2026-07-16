using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public class McpServerManagerTests
{
    [Theory]
    [InlineData("npx", "-y @modelcontextprotocol/server-filesystem")]
    [InlineData("python.exe", "-m trusted_module")]
    [InlineData("dotnet", "tool.dll")]
    public void IsSafeCommand_AcceptsWhitelistedExecutables(string command, string arguments)
    {
        Assert.True(McpServerManager.IsSafeCommand(command, arguments, out string reason));
        Assert.Equal(string.Empty, reason);
    }

    [Theory]
    [InlineData("cmd.exe", "/c whoami")]
    [InlineData("powershell.exe", "-Command whoami")]
    [InlineData(@"C:\Tools\npx.cmd", "-y package")]
    [InlineData("npx", "-y package & whoami")]
    [InlineData("npx", "-y package\nwhoami")]
    public void IsSafeCommand_RejectsShellsPathsAndControlOperators(
        string command,
        string arguments)
    {
        Assert.False(McpServerManager.IsSafeCommand(command, arguments, out string reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }
}
