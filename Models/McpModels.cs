using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace BlueSapphire.Models
{
    // ----------------------
    // JSON-RPC 基础协议
    // ----------------------
    public class JsonRpcMessage
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";
    }

    public class JsonRpcRequest : JsonRpcMessage
    {
        [JsonPropertyName("id")]
        public object Id { get; set; } = 0; // 可以是 string 或 int

        [JsonPropertyName("method")]
        public string Method { get; set; } = "";

        [JsonPropertyName("params")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonNode? Params { get; set; }
    }

    public class JsonRpcNotification : JsonRpcMessage
    {
        [JsonPropertyName("method")]
        public string Method { get; set; } = "";

        [JsonPropertyName("params")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonNode? Params { get; set; }
    }

    public class JsonRpcResponse : JsonRpcMessage
    {
        [JsonPropertyName("id")]
        public object Id { get; set; } = 0;

        [JsonPropertyName("result")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonNode? Result { get; set; }

        [JsonPropertyName("error")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonRpcError? Error { get; set; }
    }

    public class JsonRpcError
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonNode? Data { get; set; }
    }

    // ----------------------
    // MCP 具体协议定义
    // ----------------------
    
    public class McpInitializeParams
    {
        [JsonPropertyName("protocolVersion")]
        public string ProtocolVersion { get; set; } = "2024-11-05";

        [JsonPropertyName("capabilities")]
        public JsonNode Capabilities { get; set; } = JsonNode.Parse("{}")!;

        [JsonPropertyName("clientInfo")]
        public McpClientInfo ClientInfo { get; set; } = new();
    }

    public class McpClientInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "BlueSapphire";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";
    }

    public class McpToolListResult
    {
        [JsonPropertyName("tools")]
        public List<McpTool> Tools { get; set; } = new();
    }

    public class McpTool
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("inputSchema")]
        public JsonNode InputSchema { get; set; } = JsonNode.Parse("{}")!;
    }

    public class McpCallToolParams
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("arguments")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonNode? Arguments { get; set; }
    }

    public class McpCallToolResult
    {
        [JsonPropertyName("content")]
        public List<McpContent> Content { get; set; } = new();

        [JsonPropertyName("isError")]
        public bool IsError { get; set; }
    }

    public class McpContent
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "text";

        [JsonPropertyName("text")]
        public string Text { get; set; } = "";
    }
}
