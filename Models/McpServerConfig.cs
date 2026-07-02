using System.Collections.Generic;

namespace BlueSapphire.Models
{
    public class McpServerConfig
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString();
        public string Name { get; set; } = "New MCP Server";
        public string Command { get; set; } = "";
        public string Arguments { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
        
        // 可选：未来可以支持环境变量
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
    }
}
