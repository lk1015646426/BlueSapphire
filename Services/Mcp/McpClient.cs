using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BlueSapphire.Models;

namespace BlueSapphire.Services.Mcp
{
    public class McpClient : IDisposable
    {
        private const int MaxProtocolLineCharacters = 1024 * 1024;
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
        private Process? _process;
        private int _nextId = 1;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode?>> _pendingRequests = new();
        private CancellationTokenSource? _cts;
        private readonly SemaphoreSlim _stdinLock = new(1, 1);
        
        public string ServerName { get; }
        public bool IsConnected => _process != null && !_process.HasExited;

        public event Action<string>? OnLog;
        public event Action? OnExited;

        public McpClient(string serverName)
        {
            ServerName = serverName;
        }

        public async Task StartAsync(
            string command,
            string arguments,
            IDictionary<string, string>? envVars = null,
            CancellationToken cancellationToken = default)
        {
            if (IsConnected) return;

            _cts = new CancellationTokenSource();

            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (envVars != null)
            {
                foreach (var kvp in envVars)
                {
                    startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
                }
            }

            _process = new Process { StartInfo = startInfo };
            _process.EnableRaisingEvents = true;
            _process.Exited += (s, e) => 
            {
                OnLog?.Invoke($"[{ServerName}] 进程已退出");
                OnExited?.Invoke();
                FailAllPendingRequests();
            };

            _process.Start();
            BlueSapphire.Helpers.JobObjectHelper.AssignProcess(_process);

            // 异步读取 stdout (JSON-RPC)
            _ = Task.Run(async () => await ReadStdOutAsync(_cts.Token));

            // 异步读取 stderr (Logs)
            _ = Task.Run(async () => await ReadStdErrAsync(_cts.Token));

            // 发送 Initialize 请求
            await InitializeAsync(cancellationToken);
        }

        private async Task ReadStdOutAsync(CancellationToken token)
        {
            if (_process == null) return;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var line = await _process.StandardOutput.ReadLineAsync(token);
                    if (line == null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.Length > MaxProtocolLineCharacters)
                    {
                        OnLog?.Invoke($"[{ServerName}] 协议消息超过 1 MB，已终止连接。");
                        _cts?.Cancel();
                        FailAllPendingRequests();
                        break;
                    }

                    try
                    {
                        var node = JsonNode.Parse(
                            line,
                            documentOptions: new JsonDocumentOptions { MaxDepth = 32 });
                        if (node == null) continue;

                        if (node["id"] != null && (node["result"] != null || node["error"] != null))
                        {
                            // 这是一个 Response
                            var id = node["id"]!.ToString();
                            if (_pendingRequests.TryGetValue(id, out var tcs))
                            {
                                if (node["error"] != null)
                                {
                                    var errMsg = node["error"]?["message"]?.ToString() ?? "Unknown MCP Error";
                                    tcs.TrySetException(new Exception($"MCP Error: {errMsg}"));
                                }
                                else
                                {
                                    tcs.TrySetResult(node["result"]);
                                }
                                _pendingRequests.TryRemove(id, out _);
                            }
                        }
                        else if (node["method"] != null)
                        {
                            // 这是一个 Notification 或来自服务器的 Request
                            var method = node["method"]!.ToString();
                            OnLog?.Invoke($"[{ServerName}] 收到通知/请求: {method}");
                        }
                    }
                    catch (Exception ex)
                    {
                        string preview = line[..Math.Min(line.Length, 1000)];
                        OnLog?.Invoke($"[{ServerName}] 解析 JSON 失败: {ex.Message}\n内容预览: {preview}");
                    }
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[{ServerName}] 标准输出流读取结束: {ex.Message}");
            }
        }

        private async Task ReadStdErrAsync(CancellationToken token)
        {
            if (_process == null) return;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var line = await _process.StandardError.ReadLineAsync(token);
                    if (line == null) break;
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        OnLog?.Invoke($"[{ServerName} stderr] {line[..Math.Min(line.Length, 4000)]}");
                    }
                }
            }
            catch
            {
                // 读取循环随取消/进程退出结束，日志转储尽力而为。
            }
        }

        private async Task<JsonNode?> SendRequestAsync(
            string method,
            JsonNode? parameters,
            CancellationToken cancellationToken)
        {
            if (!IsConnected || _process == null) throw new InvalidOperationException("MCP Server is not connected.");

            var id = Interlocked.Increment(ref _nextId).ToString();
            var tcs = new TaskCompletionSource<JsonNode?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests[id] = tcs;

            var request = new JsonRpcRequest
            {
                Id = id,
                Method = method,
                Params = parameters
            };

            var json = JsonSerializer.Serialize(request);
            await _stdinLock.WaitAsync(cancellationToken);
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    await _process.StandardInput.WriteLineAsync(json);
                    await _process.StandardInput.FlushAsync();
                }
                else
                {
                    throw new InvalidOperationException("MCP Server is not connected.");
                }
            }
            catch
            {
                _pendingRequests.TryRemove(id, out _);
                throw;
            }
            finally
            {
                _stdinLock.Release();
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _cts?.Token ?? CancellationToken.None);
            timeoutCts.CancelAfter(RequestTimeout);
            using CancellationTokenRegistration registration = timeoutCts.Token.Register(
                () => tcs.TrySetCanceled(timeoutCts.Token));
            try
            {
                return await tcs.Task;
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested &&
                !(_cts?.IsCancellationRequested ?? false))
            {
                throw new TimeoutException($"MCP request '{method}' timed out.");
            }
            finally
            {
                _pendingRequests.TryRemove(id, out _);
            }
        }

        private async Task InitializeAsync(CancellationToken cancellationToken)
        {
            var p = new McpInitializeParams();
            await SendRequestAsync(
                "initialize",
                JsonSerializer.SerializeToNode(p),
                cancellationToken);
            // 收到 initialize response 后，按标准我们需要发一个 notifications/initialized
            var initNotification = new JsonRpcNotification { Method = "notifications/initialized" };
            var json = JsonSerializer.Serialize(initNotification);
            await _stdinLock.WaitAsync(cancellationToken);
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    await _process.StandardInput.WriteLineAsync(json);
                    await _process.StandardInput.FlushAsync();
                }
            }
            finally
            {
                _stdinLock.Release();
            }
        }

        public async Task<List<McpTool>> GetToolsAsync(CancellationToken cancellationToken = default)
        {
            var resultNode = await SendRequestAsync("tools/list", null, cancellationToken);
            if (resultNode == null) return new List<McpTool>();

            var listResult = resultNode.Deserialize<McpToolListResult>();
            return listResult?.Tools ?? new List<McpTool>();
        }

        public async Task<McpCallToolResult> CallToolAsync(
            string name,
            JsonNode? arguments,
            CancellationToken cancellationToken = default)
        {
            var args = new McpCallToolParams { Name = name, Arguments = arguments };
            var resultNode = await SendRequestAsync(
                "tools/call",
                JsonSerializer.SerializeToNode(args),
                cancellationToken);
            
            if (resultNode == null) return new McpCallToolResult { IsError = true, Content = new List<McpContent> { new McpContent { Text = "Empty response" } } };

            return resultNode.Deserialize<McpCallToolResult>() ?? new McpCallToolResult { IsError = true, Content = new List<McpContent> { new McpContent { Text = "Failed to deserialize result" } } };
        }

        private void FailAllPendingRequests()
        {
            foreach (var kvp in _pendingRequests)
            {
                kvp.Value.TrySetException(new Exception("MCP Process Exited"));
            }
            _pendingRequests.Clear();
        }

        public void Dispose()
        {
            _cts?.Cancel();
            FailAllPendingRequests();

            if (_process != null)
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill(true);
                    }
                    _process.Dispose();
                }
                catch
                {
                    // 进程已自行退出或句柄失效时，Kill/Dispose 允许失败。
                }
                _process = null;
            }
            // 释放路径兜底：已释放对象重复 Dispose 不应掩盖主流程。
            try { _stdinLock.Dispose(); } catch { }
            try { _cts?.Dispose(); } catch { }
        }
    }
}
