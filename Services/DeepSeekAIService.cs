using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BlueSapphire.Helpers;

namespace BlueSapphire.Services
{
    public class DeepSeekAIService
    {
        private const int MaxJsonResponseBytes = 4 * 1024 * 1024;
        private const int MaxErrorResponseBytes = 32 * 1024;
        private const int MaxStreamCharacters = 8 * 1024 * 1024;
        private const int MaxStreamLineCharacters = 1024 * 1024;
        private readonly IHttpClientFactory _httpClientFactory;

        public DeepSeekAIService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<ChatMessage> SendChatAsync(List<ChatMessage> messages, List<ChatTool>? tools = null, CancellationToken cancellationToken = default)
        {
            string provider = AppSettings.Get("DeepSeekApiProvider", "Official") ?? "Official";
            string? apiKey = GetApiKey(provider);

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new ChatMessage { Role = "assistant", Content = "错误：请先在设置中配置对应的 API Key。" };
            }

            string apiUrl = provider == "SiliconFlow" 
                ? "https://api.siliconflow.cn/v1/chat/completions" 
                : "https://api.deepseek.com/v1/chat/completions";
            
            string defaultModel = provider == "SiliconFlow" ? "deepseek-ai/DeepSeek-V3" : "deepseek-chat";
            string modelName = AppSettings.Get($"DeepSeekApiModel_{provider}", AppSettings.Get("DeepSeekApiModel", defaultModel)) ?? defaultModel;
            if (string.IsNullOrWhiteSpace(modelName))
            {
                modelName = defaultModel;
            }

            var requestBody = new
            {
                model = modelName,
                messages = messages,
                tools = tools,
                temperature = 0.6
            };

            var requestJson = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });

            using var jsonContent = new StringContent(
                requestJson,
                Encoding.UTF8,
                "application/json");

            HttpClient httpClient = _httpClientFactory.CreateClient("DeepSeek");

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl)
                {
                    Content = jsonContent
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                using HttpResponseMessage response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                string responseString = await NetworkSafety.ReadContentAsStringAsync(
                    response.Content,
                    response.IsSuccessStatusCode ? MaxJsonResponseBytes : MaxErrorResponseBytes,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return new ChatMessage
                    {
                        Role = "assistant",
                        Content = $"API 请求失败：{(int)response.StatusCode} {response.ReasonPhrase}\n{responseString}"
                    };
                }

                using var jsonDoc = JsonDocument.Parse(responseString);
                var choice = jsonDoc.RootElement.GetProperty("choices")[0];
                var message = choice.GetProperty("message");

                return JsonSerializer.Deserialize<ChatMessage>(message.GetRawText()) ?? new ChatMessage { Role = "assistant", Content = "API 解析失败" };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (JsonException)
            {
                return new ChatMessage { Role = "assistant", Content = "API 返回了无法解析的数据。" };
            }
            catch (Exception ex)
            {
                return new ChatMessage { Role = "assistant", Content = $"发生错误: {ex.Message}" };
            }
        }

        public async Task<(List<string> Models, string? Error)> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
        {
            string provider = AppSettings.Get("DeepSeekApiProvider", "Official") ?? "Official";
            string? apiKey = GetApiKey(provider);

            if (string.IsNullOrWhiteSpace(apiKey)) return (new List<string>(), "API Key 未配置");

            string apiUrl = provider == "SiliconFlow" 
                ? "https://api.siliconflow.cn/v1/models" 
                : "https://api.deepseek.com/v1/models";

            HttpClient httpClient = _httpClientFactory.CreateClient("DeepSeek");

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                using HttpResponseMessage response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                string responseString = await NetworkSafety.ReadContentAsStringAsync(
                    response.Content,
                    response.IsSuccessStatusCode ? MaxJsonResponseBytes : MaxErrorResponseBytes,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return (new List<string>(), $"请求失败 ({(int)response.StatusCode} {response.ReasonPhrase}): {responseString}");
                }

                using var jsonDoc = JsonDocument.Parse(responseString);
                
                var models = new List<string>();
                if (jsonDoc.RootElement.TryGetProperty("data", out var dataElement))
                {
                    foreach (var item in dataElement.EnumerateArray())
                    {
                        if (item.TryGetProperty("id", out var idElement))
                        {
                            string id = idElement.GetString() ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(id) && id.Length <= 200)
                            {
                                models.Add(id);
                            }
                        }
                    }
                }
                return (models.Distinct(StringComparer.Ordinal).Take(500).ToList(), null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (JsonException)
            {
                return (new List<string>(), "服务返回了无法解析的模型列表。");
            }
            catch (Exception ex)
            {
                return (new List<string>(), $"网络异常: {ex.Message}");
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var result = await GetAvailableModelsAsync();
                return result.Models.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        public async IAsyncEnumerable<ChatStreamEvent> SendChatStreamAsync(List<ChatMessage> messages, List<ChatTool>? tools = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            string provider = AppSettings.Get("DeepSeekApiProvider", "Official") ?? "Official";
            string? apiKey = GetApiKey(provider);

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                yield return new ChatStreamEvent { ContentDelta = "错误：请先在设置中配置对应的 API Key。" };
                yield break;
            }

            string apiUrl = provider == "SiliconFlow" 
                ? "https://api.siliconflow.cn/v1/chat/completions" 
                : "https://api.deepseek.com/v1/chat/completions";
            
            string defaultModel = provider == "SiliconFlow" ? "deepseek-ai/DeepSeek-V3" : "deepseek-chat";
            string modelName = AppSettings.Get($"DeepSeekApiModel_{provider}", AppSettings.Get("DeepSeekApiModel", defaultModel)) ?? defaultModel;
            if (string.IsNullOrWhiteSpace(modelName))
            {
                modelName = defaultModel;
            }

            var requestBody = new
            {
                model = modelName,
                messages = messages,
                tools = tools,
                temperature = 0.6,
                stream = true
            };

            var requestJson = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
            var jsonContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

            HttpClient httpClient = _httpClientFactory.CreateClient("DeepSeek");

            using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl) { Content = jsonContent };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                string err = await NetworkSafety.ReadContentAsStringAsync(
                    response.Content,
                    MaxErrorResponseBytes,
                    cancellationToken);
                yield return new ChatStreamEvent
                {
                    ContentDelta = $"API 请求失败：{(int)response.StatusCode} {response.ReasonPhrase}\n{err}"
                };
                yield break;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new System.IO.StreamReader(stream);

            int streamedCharacters = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                streamedCharacters += line.Length;
                if (line.Length > MaxStreamLineCharacters ||
                    streamedCharacters > MaxStreamCharacters)
                {
                    yield return new ChatStreamEvent { ContentDelta = "\n响应内容过长，已停止继续读取。" };
                    yield break;
                }
                if (line.StartsWith("data: "))
                {
                    var data = line.Substring(6);
                    if (data == "[DONE]") break;

                    ChatStreamEvent streamEvent = new ChatStreamEvent();
                    bool hasData = false;

                    try
                    {
                        using var jsonDoc = JsonDocument.Parse(data);
                        var choices = jsonDoc.RootElement.GetProperty("choices");
                        if (choices.GetArrayLength() > 0)
                        {
                            var delta = choices[0].GetProperty("delta");
                            
                            if (delta.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String)
                            {
                                streamEvent.ContentDelta = contentProp.GetString();
                                hasData = true;
                            }
                            if (delta.TryGetProperty("role", out var roleProp) && roleProp.ValueKind == JsonValueKind.String)
                            {
                                streamEvent.Role = roleProp.GetString();
                                hasData = true;
                            }
                            if (delta.TryGetProperty("tool_calls", out var toolCallsProp) && toolCallsProp.ValueKind == JsonValueKind.Array)
                            {
                                streamEvent.ToolCallFragments = new List<ToolCallFragment>();
                                foreach (var tc in toolCallsProp.EnumerateArray())
                                {
                                    var frag = new ToolCallFragment();
                                    if (tc.TryGetProperty("index", out var indexProp)) frag.Index = indexProp.GetInt32();
                                    if (tc.TryGetProperty("id", out var idProp)) frag.Id = idProp.GetString();
                                    if (tc.TryGetProperty("type", out var typeProp)) frag.Type = typeProp.GetString();
                                    if (tc.TryGetProperty("function", out var funcProp))
                                    {
                                        if (funcProp.TryGetProperty("name", out var nameProp)) frag.FunctionName = nameProp.GetString();
                                        if (funcProp.TryGetProperty("arguments", out var argProp)) frag.FunctionArgumentsDelta = argProp.GetString();
                                    }
                                    streamEvent.ToolCallFragments.Add(frag);
                                }
                                hasData = true;
                            }
                        }
                    }
                    catch
                    {
                        // 跳过无法解析的 SSE 数据行，保持流式输出连续。
                    }

                    if (hasData)
                    {
                        yield return streamEvent;
                    }
                }
            }
        }

        private static string? GetApiKey(string provider)
        {
            string? providerKey = AppSettings.GetSecret($"DeepSeekApiKey_{provider}");
            if (!string.IsNullOrWhiteSpace(providerKey))
            {
                return providerKey;
            }

            return string.Equals(provider, "Official", StringComparison.OrdinalIgnoreCase)
                ? AppSettings.GetSecret("DeepSeekApiKey")
                : null;
        }
    }

    public class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Content { get; set; }
        
        [JsonPropertyName("name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; set; }

        [JsonPropertyName("tool_calls")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonElement? ToolCalls { get; set; }

        [JsonPropertyName("tool_call_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolCallId { get; set; }
    }

    public class ChatTool
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "function";

        [JsonPropertyName("function")]
        public ChatFunction Function { get; set; } = new();
    }

    public class ChatFunction
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("parameters")]
        public object? Parameters { get; set; }
    }

    public class ChatStreamEvent
    {
        public string? ContentDelta { get; set; }
        public string? Role { get; set; }
        public List<ToolCallFragment>? ToolCallFragments { get; set; }
    }

    public class ToolCallFragment
    {
        public int Index { get; set; }
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? FunctionName { get; set; }
        public string? FunctionArgumentsDelta { get; set; }
    }
}
