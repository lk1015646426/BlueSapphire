using System;
using Microsoft.UI.Xaml.Controls; // 用于 Symbol
using BlueSapphire.Interfaces;      // 用于 ITool
using BlueSapphire.Tools;           // 命名空间建议
using BlueSapphire.Models;
using System.Collections.Generic;

namespace BlueSapphire.Tools
{
    public class MediaManagerTool : ITool, IAIToolCapabilityProvider
    {
        public string Id => "MediaManager";
        public string ToolId => Id;
        public string Title => "媒体工作台";
        public Symbol Icon => Symbol.Pictures;

        // 关键点：这里只返回类型 (typeof)，不实例化 Page
        public Type ContentPage => typeof(MediaManagerPage);

        public void Initialize()
        {
            // 如果有初始化逻辑写在这里
        }

        public IReadOnlyList<AIToolCapabilityDefinition> GetCapabilities() =>
        [
            new()
            {
                ToolId = Id,
                Name = "analyze_media_folder",
                RiskLevel = AIToolRiskLevel.ReadOnly,
                SupportsCancellation = true,
                SupportsProgress = true
            },
            new()
            {
                ToolId = Id,
                Name = "preview_media_organization",
                RiskLevel = AIToolRiskLevel.ReadOnly,
                SupportsPreview = true
            },
            new()
            {
                ToolId = Id,
                Name = "execute_exact_duplicate_cleanup",
                RiskLevel = AIToolRiskLevel.Destructive,
                SupportsCancellation = true,
                SupportsProgress = true
            },
            new()
            {
                ToolId = Id,
                Name = "execute_media_organization",
                RiskLevel = AIToolRiskLevel.Destructive,
                SupportsCancellation = true,
                SupportsProgress = true
            }
        ];
    }
}
