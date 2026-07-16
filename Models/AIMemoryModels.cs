using System;
using System.Collections.Generic;

namespace BlueSapphire.Models
{
    public enum AIMemoryScope
    {
        Global,
        Cleanup,
        Media,
        Writing
    }

    public sealed class AIMemoryEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Content { get; set; } = string.Empty;
        public AIMemoryScope Scope { get; set; } = AIMemoryScope.Global;
        public string Source { get; set; } = "用户确认";
        public bool IsEnabled { get; set; } = true;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
        public DateTimeOffset? ExpiresAt { get; set; }

        public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value <= DateTimeOffset.Now;
        public string ScopeText => Scope switch
        {
            AIMemoryScope.Cleanup => "清理",
            AIMemoryScope.Media => "媒体",
            AIMemoryScope.Writing => "写作",
            _ => "全局"
        };
        public string StatusText => IsExpired ? "已过期" : IsEnabled ? "已启用" : "已停用";
        public string ExpiryText => ExpiresAt.HasValue
            ? $"有效期至 {ExpiresAt.Value.ToLocalTime():yyyy-MM-dd}"
            : "长期有效";
    }

    public sealed class AIMemoryState
    {
        public int Version { get; set; } = 2;
        public bool IsPaused { get; set; }
        public List<AIMemoryEntry> Entries { get; set; } = new();
    }
}
