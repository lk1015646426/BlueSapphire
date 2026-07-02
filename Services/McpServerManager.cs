using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BlueSapphire.Models;

namespace BlueSapphire.Services
{
    public class McpServerManager : IDisposable
    {
        private readonly string _configFilePath;
        private readonly ConcurrentDictionary<string, McpClient> _clients = new();
        private readonly List<McpServerConfig> _configs = new();
        public event Action? OnServersChanged;

        public McpServerManager()
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlueSapphire");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            _configFilePath = Path.Combine(folder, "mcp_servers.json");
            
            LoadConfigs();
        }

        private void LoadConfigs()
        {
            if (File.Exists(_configFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_configFilePath);
                    var list = JsonSerializer.Deserialize<List<McpServerConfig>>(json);
                    if (list != null)
                    {
                        _configs.Clear();
                        _configs.AddRange(list);
                    }
                }
                catch { }
            }
        }

        private void SaveConfigs()
        {
            try
            {
                var json = JsonSerializer.Serialize(_configs, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configFilePath, json);
                OnServersChanged?.Invoke();
            }
            catch { }
        }

        public IReadOnlyList<McpServerConfig> GetServers() => _configs.AsReadOnly();

        public void AddOrUpdateServer(McpServerConfig config)
        {
            var existing = _configs.FirstOrDefault(c => c.Id == config.Id);
            if (existing != null)
            {
                existing.Name = config.Name;
                existing.Command = config.Command;
                existing.Arguments = config.Arguments;
                existing.IsEnabled = config.IsEnabled;
                existing.EnvironmentVariables = config.EnvironmentVariables;
            }
            else
            {
                _configs.Add(config);
            }
            SaveConfigs();
        }

        public void RemoveServer(string id)
        {
            var config = _configs.FirstOrDefault(c => c.Id == id);
            if (config != null)
            {
                StopServer(id);
                _configs.Remove(config);
                SaveConfigs();
            }
        }

        public async Task StartAllEnabledServersAsync()
        {
            foreach (var config in _configs.Where(c => c.IsEnabled))
            {
                await StartServerAsync(config.Id);
            }
        }

        public async Task StartServerAsync(string id)
        {
            var config = _configs.FirstOrDefault(c => c.Id == id);
            if (config == null || string.IsNullOrWhiteSpace(config.Command)) return;

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
                await client.StartAsync(config.Command, config.Arguments, config.EnvironmentVariables);
                OnServersChanged?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start MCP Server {config.Name}: {ex.Message}");
                _clients.TryRemove(id, out _);
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

        // 获取所有的工具 (带有 server_id_前缀)
        public async Task<List<(string ServerId, McpTool Tool)>> GetAllToolsAsync()
        {
            var allTools = new List<(string, McpTool)>();
            foreach (var kvp in _clients)
            {
                if (kvp.Value.IsConnected)
                {
                    try
                    {
                        var tools = await kvp.Value.GetToolsAsync();
                        foreach (var tool in tools)
                        {
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

        public async Task<string> CallToolAsync(string serverId, string toolName, string argumentsJson)
        {
            if (_clients.TryGetValue(serverId, out var client) && client.IsConnected)
            {
                JsonNode? argsNode = null;
                if (!string.IsNullOrWhiteSpace(argumentsJson))
                {
                    try { argsNode = JsonNode.Parse(argumentsJson); } catch { }
                }

                var result = await client.CallToolAsync(toolName, argsNode);
                
                if (result.Content != null && result.Content.Count > 0)
                {
                    var textContent = string.Join("\n", result.Content.Select(c => c.Text));
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
