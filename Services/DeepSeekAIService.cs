using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BlueSapphire.Helpers;

namespace BlueSapphire.Services
{
    public class DeepSeekAIService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private const string ApiUrl = "https://api.deepseek.com/v1/chat/completions";

        public DeepSeekAIService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<ChatMessage> SendChatAsync(List<ChatMessage> messages, List<ChatTool>? tools = null)
        {
            string? apiKey = AppSettings.GetSecret("DeepSeekApiKey");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new ChatMessage { Role = "assistant", Content = "错误：请先在设置中配置 DeepSeek API Key。" };
            }

            var requestBody = new
            {
                model = "deepseek-chat",
                messages = messages,
                tools = tools,
                temperature = 0.6
            };

            var requestJson = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });

            var jsonContent = new StringContent(
                requestJson,
                Encoding.UTF8,
                "application/json");

            HttpClient httpClient = _httpClientFactory.CreateClient("DeepSeek");
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            try
            {
                var response = await httpClient.PostAsync(ApiUrl, jsonContent);
                var responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return new ChatMessage { Role = "assistant", Content = $"API 请求失败: {response.StatusCode}\n{responseString}" };
                }

                var jsonDoc = JsonDocument.Parse(responseString);
                var choice = jsonDoc.RootElement.GetProperty("choices")[0];
                var message = choice.GetProperty("message");

                return JsonSerializer.Deserialize<ChatMessage>(message.GetRawText()) ?? new ChatMessage { Role = "assistant", Content = "API 解析失败" };
            }
            catch (Exception ex)
            {
                return new ChatMessage { Role = "assistant", Content = $"发生错误: {ex.Message}" };
            }
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
}
