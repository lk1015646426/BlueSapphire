using BlueSapphire.Models;
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

    [Fact]
    public async Task ConcurrentAddEnumerateAndRemove_NoExceptionsOrLostUpdates()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), "BlueSapphireMcpTests", Guid.NewGuid().ToString("N"));
        try
        {
            using McpServerManager manager = new(Path.Combine(rootPath, "mcp_servers.json"));

            // 未批准（IsApproved=false）的服务器不会被启动，并发启动扫描不会拉起进程。
            Task[] adders = Enumerable.Range(0, 8).Select(i => Task.Run(() =>
                manager.AddOrUpdateServer(new McpServerConfig
                {
                    Id = $"srv-{i}",
                    Name = $"Server {i}",
                    Command = "npx",
                    Arguments = $"-y pkg{i}",
                    IsApproved = false
                }))).ToArray();

            Task[] enumerators = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                for (int i = 0; i < 50; i++)
                {
                    foreach (McpServerConfig server in manager.GetServers())
                    {
                        Assert.False(string.IsNullOrEmpty(server.Name));
                    }
                    await manager.StartAllEnabledServersAsync();
                }
            })).ToArray();

            Task[] removers = Enumerable.Range(0, 2).Select(i => Task.Run(async () =>
            {
                // 与新增并发地反复删除，制造真实的增删竞争。
                for (int round = 0; round < 3; round++)
                {
                    manager.RemoveServer($"srv-{i}");
                    await Task.Yield();
                }
            })).ToArray();

            await Task.WhenAll(adders.Concat(enumerators).Concat(removers));
            // 并发阶段结束后补删一次，使最终数量可确定断言。
            manager.RemoveServer("srv-0");
            manager.RemoveServer("srv-1");

            // 8 个新增 - 2 个删除（srv-0/srv-1）= 6。
            Assert.Equal(6, manager.GetServers().Count);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, true);
            }
        }
    }
}
