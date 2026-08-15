using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using BlueSapphire.Models;

namespace BlueSapphire.Services
{
    public class McpServerManager : IDisposable
    {
        private const int MaxServers = 16;
        private const int MaxToolsPerServer = 64;
        private const int MaxToolArgumentsCharacters = 128 * 1024;
        private const int MaxToolResultCharacters = 128 * 1024;
        private const long MaxConfigBytes = 2 * 1024 * 1024;
        private readonly string _configFilePath;
        private readonly ConcurrentDictionary<string, McpClient> _clients = new();
        // _configs 被 UI 线程（增删改/枚举）与后台（启动扫描/工具枚举）并发访问，
        // 所有读写必须持 _configLock；跨 await 的长操作先在锁内取快照再在锁外执行。
        private readonly object _configLock = new();
        private readonly List<McpServerConfig> _configs = new();
        private static readonly HashSet<string> AllowedCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            "npx", "npx.cmd", "uvx", "uvx.exe", "node", "node.exe",
            "python", "python.exe", "python3", "python3.exe", "py", "py.exe",
            "dotnet", "dotnet.exe"
        };
        private static readonly Regex EnvironmentNamePattern = new(
            "^[A-Za-z_][A-Za-z0-9_]{0,127}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        public event Action? OnServersChanged;

        public McpServerManager(string? configFilePath = null)
        {
            if (configFilePath == null)
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlueSapphire");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                _configFilePath = Path.Combine(folder, "mcp_servers.json");
            }
            else
            {
                string? directory = Path.GetDirectoryName(configFilePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                _configFilePath = configFilePath;
            }

            LoadConfigs();
        }

        private void LoadConfigs()
        {
            if (File.Exists(_configFilePath))
            {
                try
                {
                    if (new FileInfo(_configFilePath).Length is <= 0 or > MaxConfigBytes)
                    {
                        return;
                    }
                    var json = File.ReadAllText(_configFilePath);
                    var list = JsonSerializer.Deserialize<List<McpServerConfig>>(json);
                    if (list != null)
                    {
                        bool migratedPlaintextSecrets = RestoreEnvironmentVariables(json, list);
                        _configs.Clear();
                        foreach (McpServerConfig config in list.Take(MaxServers))
                        {
                            try
                            {
                                ValidateConfig(config);
                                if (_configs.All(existing =>
                                        !string.Equals(existing.Id, config.Id, StringComparison.OrdinalIgnoreCase)))
                                {
                                    _configs.Add(config);
                                }
                            }
                            catch
                            {
                                // 损坏或越界的持久化配置不进入运行时。
                            }
                        }
                        if (migratedPlaintextSecrets)
                        {
                            SaveConfigs();
                        }
                    }
                }
                catch { }
            }
        }

        private void SaveConfigs()
        {
            bool saved = false;
            // 全程持锁：序列化读取与临时文件写替换必须串行，避免并发保存写坏配置文件。
            // 事件在锁外触发，防止订阅者同步回调本类方法时长时间持锁。
            lock (_configLock)
            {
                try
                {
                    foreach (McpServerConfig config in _configs)
                    {
                        config.ProtectedEnvironmentVariables = ProtectEnvironmentVariables(config.EnvironmentVariables);
                    }

                    var json = JsonSerializer.Serialize(_configs, new JsonSerializerOptions { WriteIndented = true });
                    string tempPath = _configFilePath + ".tmp";
                    File.WriteAllText(tempPath, json);
                    File.Move(tempPath, _configFilePath, true);
                    saved = true;
                }
                catch
                {
                    // 写盘失败保持内存态，下次保存重试。
                }
            }

            if (saved)
            {
                OnServersChanged?.Invoke();
            }
        }

        public IReadOnlyList<McpServerConfig> GetServers()
        {
            // 返回快照而非包装：调用方枚举期间集合可能被增删。
            lock (_configLock)
            {
                return _configs.ToList();
            }
        }

        public void AddOrUpdateServer(McpServerConfig config)
        {
            ValidateConfig(config);
            lock (_configLock)
            {
                var existing = _configs.FirstOrDefault(c => c.Id == config.Id);
                if (existing != null)
                {
                    existing.Name = config.Name;
                    existing.Command = config.Command;
                    existing.Arguments = config.Arguments;
                    existing.IsEnabled = config.IsEnabled;
                    existing.IsApproved = config.IsApproved;
                    existing.EnvironmentVariables = config.EnvironmentVariables;
                }
                else
                {
                    if (_configs.Count >= MaxServers)
                    {
                        throw new InvalidOperationException($"最多只能保存 {MaxServers} 个 MCP 服务器。");
                    }
                    _configs.Add(config);
                }
            }
            SaveConfigs();
        }

        public void RemoveServer(string id)
        {
            bool removed = false;
            lock (_configLock)
            {
                var config = _configs.FirstOrDefault(c => c.Id == id);
                if (config != null)
                {
                    _configs.Remove(config);
                    removed = true;
                }
            }
            if (removed)
            {
                StopServer(id);
                SaveConfigs();
            }
        }

        public async Task StartAllEnabledServersAsync()
        {
            List<McpServerConfig> toStart;
            lock (_configLock)
            {
                toStart = _configs.Where(c => c.IsEnabled && c.IsApproved).ToList();
            }
            foreach (var config in toStart)
            {
                await StartServerAsync(config.Id);
            }
        }

        public async Task StartServerAsync(
            string id,
            CancellationToken cancellationToken = default)
        {
            McpServerConfig? config;
            lock (_configLock)
            {
                config = _configs.FirstOrDefault(c => c.Id == id);
            }
            if (config == null || !config.IsApproved || string.IsNullOrWhiteSpace(config.Command)) return;
            ValidateConfig(config);

            if (_clients.TryGetValue(id, out var existingClient) && existingClient.IsConnected)
            {
                return; // 已经运行
            }

            var client = new McpClient(config.Name);
            // 订阅事件供调试或展示
            client.OnLog += (msg) => System.Diagnostics.Debug.WriteLine(msg);
            client.OnExited += () => 
            {
                _clients.TryRemove(id, out _);
                OnServersChanged?.Invoke();
            };

            _clients[id] = client;
            
            try
            {
                await client.StartAsync(
                    config.Command,
                    config.Arguments,
                    config.EnvironmentVariables,
                    cancellationToken);
                OnServersChanged?.Invoke();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _clients.TryRemove(id, out _);
                client.Dispose();
                OnServersChanged?.Invoke();
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start MCP Server {config.Name}: {ex.Message}");
                _clients.TryRemove(id, out _);
                client.Dispose();
                OnServersChanged?.Invoke();
            }
        }

        public void StopServer(string id)
        {
            if (_clients.TryGetValue(id, out var client))
            {
                client.Dispose();
                _clients.TryRemove(id, out _);
                OnServersChanged?.Invoke();
            }
        }

        public bool IsServerRunning(string id)
        {
            return _clients.TryGetValue(id, out var client) && client.IsConnected;
        }

        public static bool IsSafeCommand(string command, string arguments, out string reason)
        {
            string normalizedCommand = (command ?? string.Empty).Trim();
            arguments ??= string.Empty;
            if (normalizedCommand.Length == 0 ||
                normalizedCommand.IndexOfAny(new[] { '\\', '/', ':', '"', '\'' }) >= 0 ||
                normalizedCommand.Any(char.IsWhiteSpace) ||
                !AllowedCommands.Contains(normalizedCommand))
            {
                reason = "仅允许使用 npx、uvx、node、python、py 或 dotnet 的标准命令名，不能填写路径。";
                return false;
            }

            if (arguments.Length > 4096)
            {
                reason = "启动参数过长。";
                return false;
            }

            string[] blockedTokens = { "\r", "\n", "\0", ";", "|", "&", "`", "$(", "${", ">" , "<" };
            string? blocked = blockedTokens.FirstOrDefault(token => arguments.Contains(token, StringComparison.Ordinal));
            if (blocked != null)
            {
                reason = $"启动参数包含不允许的符号：{blocked}";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static void ValidateConfig(McpServerConfig config)
        {
            config.Id ??= string.Empty;
            config.Name ??= string.Empty;
            config.Command ??= string.Empty;
            config.Arguments ??= string.Empty;
            config.EnvironmentVariables ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!IsSafeCommand(config.Command, config.Arguments, out string reason))
            {
                throw new InvalidOperationException(reason);
            }

            if (string.IsNullOrWhiteSpace(config.Id) || config.Id.Length > 100)
            {
                throw new InvalidOperationException("MCP 服务器标识无效。");
            }

            if (string.IsNullOrWhiteSpace(config.Name) ||
                config.Name.Length > 100 ||
                config.Name.Any(char.IsControl))
            {
                throw new InvalidOperationException("MCP 服务器名称不能为空且不能超过 100 个字符。");
            }

            if (config.EnvironmentVariables.Count > 64 ||
                config.EnvironmentVariables.Any(pair =>
                    !EnvironmentNamePattern.IsMatch(pair.Key) ||
                    pair.Value.Length > 8192))
            {
                throw new InvalidOperationException("环境变量名称、数量或内容长度不符合安全限制。");
            }
        }

        private static string ProtectEnvironmentVariables(IReadOnlyDictionary<string, string> values)
        {
            if (values.Count == 0)
            {
                return string.Empty;
            }

            byte[] plaintext = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values));
            byte[] protectedBytes = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        private static Dictionary<string, string> UnprotectEnvironmentVariables(string protectedValue)
        {
            if (string.IsNullOrWhiteSpace(protectedValue))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            byte[] protectedBytes = Convert.FromBase64String(protectedValue);
            byte[] plaintext = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(plaintext))
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static bool RestoreEnvironmentVariables(string originalJson, IReadOnlyList<McpServerConfig> configs)
        {
            bool migrated = false;
            using JsonDocument document = JsonDocument.Parse(originalJson);
            JsonElement[] entries = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().ToArray()
                : Array.Empty<JsonElement>();

            for (int index = 0; index < configs.Count; index++)
            {
                McpServerConfig config = configs[index];
                try
                {
                    config.EnvironmentVariables = UnprotectEnvironmentVariables(config.ProtectedEnvironmentVariables);
                    if (config.EnvironmentVariables.Count == 0 &&
                        index < entries.Length &&
                        entries[index].TryGetProperty("EnvironmentVariables", out JsonElement legacy) &&
                        legacy.ValueKind == JsonValueKind.Object)
                    {
                        config.EnvironmentVariables = legacy.EnumerateObject()
                            .Where(property => property.Value.ValueKind == JsonValueKind.String)
                            .ToDictionary(
                                property => property.Name,
                                property => property.Value.GetString() ?? string.Empty,
                                StringComparer.OrdinalIgnoreCase);
                        migrated = config.EnvironmentVariables.Count > 0;
                    }
                }
                catch
                {
                    config.EnvironmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                // 升级前的配置没有明确授权记录，不能自动启动。
                if (!config.IsApproved)
                {
                    migrated |= config.IsEnabled;
                    config.IsEnabled = false;
                }
            }

            return migrated;
        }

        // 获取所有的工具 (带有 server_id_前缀)
        public async Task<List<(string ServerId, McpTool Tool)>> GetAllToolsAsync(
            CancellationToken cancellationToken = default)
        {
            var allTools = new List<(string, McpTool)>();
            foreach (var kvp in _clients)
            {
                if (kvp.Value.IsConnected)
                {
                    try
                    {
                        var tools = await kvp.Value.GetToolsAsync(cancellationToken);
                        foreach (var tool in tools.Take(MaxToolsPerServer))
                        {
                            if (string.IsNullOrWhiteSpace(tool.Name) || tool.Name.Length > 200)
                            {
                                continue;
                            }
                            allTools.Add((kvp.Key, tool));
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error getting tools from {kvp.Value.ServerName}: {ex.Message}");
                    }
                }
            }
            return allTools;
        }

        public async Task<string> CallToolAsync(
            string serverId,
            string toolName,
            string argumentsJson,
            CancellationToken cancellationToken = default)
        {
            if (_clients.TryGetValue(serverId, out var client) && client.IsConnected)
            {
                if (string.IsNullOrWhiteSpace(toolName) ||
                    toolName.Length > 200 ||
                    argumentsJson.Length > MaxToolArgumentsCharacters)
                {
                    return "错误：MCP 工具名称或参数超过安全限制。";
                }

                JsonNode? argsNode = null;
                if (!string.IsNullOrWhiteSpace(argumentsJson))
                {
                    try
                    {
                        argsNode = JsonNode.Parse(
                            argumentsJson,
                            documentOptions: new JsonDocumentOptions { MaxDepth = 32 });
                        if (argsNode is not JsonObject)
                        {
                            return "错误：MCP 工具参数必须是 JSON 对象。";
                        }
                    }
                    catch
                    {
                        return "错误：MCP 工具参数不是有效的 JSON。";
                    }
                }

                var result = await client.CallToolAsync(toolName, argsNode, cancellationToken);
                
                if (result.Content != null && result.Content.Count > 0)
                {
                    var textContent = string.Join(
                        "\n",
                        result.Content.Take(64).Select(c => c.Text ?? string.Empty));
                    if (textContent.Length > MaxToolResultCharacters)
                    {
                        textContent = textContent[..MaxToolResultCharacters] +
                                      "\n...[MCP 返回内容过长，已截断]";
                    }
                    if (result.IsError) return $"[MCP ERROR] {textContent}";
                    return textContent;
                }
                return "执行成功，无返回内容。";
            }
            return $"错误：未找到名为 {serverId} 的运行中服务器，或其已断开连接。";
        }

        public void Dispose()
        {
            foreach (var client in _clients.Values)
            {
                client.Dispose();
            }
            _clients.Clear();
        }
    }
}
