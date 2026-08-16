using BlueSapphire.Interfaces;
using BlueSapphire.Views;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using BlueSapphire.Models;

namespace BlueSapphire.Tools
{
    public class CleanerAssistantTool : ITool, IAIToolCapabilityProvider
    {
        public string Id => "CleanerAssistant";
        public string ToolId => Id;
        public string Title => "清理工具";
        public Symbol Icon => Symbol.Delete;
        public Type ContentPage => typeof(CleanerAssistantPage);

        public void Initialize()
        {
        }

        public IReadOnlyList<AIToolCapabilityDefinition> GetCapabilities() =>
        [
            new()
            {
                ToolId = Id,
                Name = "start_smart_cleanup",
                RiskLevel = AIToolRiskLevel.ReadOnly,
                SupportsCancellation = true,
                SupportsProgress = true
            },
            new()
            {
                ToolId = Id,
                Name = "analyze_latest_cleanup_log",
                RiskLevel = AIToolRiskLevel.ReadOnly
            },
            new()
            {
                ToolId = Id,
                Name = "execute_cleanup",
                RiskLevel = AIToolRiskLevel.Destructive,
                SupportsCancellation = true,
                SupportsProgress = true
            },
            new()
            {
                ToolId = Id,
                Name = "create_cleaner_rule_draft",
                RiskLevel = AIToolRiskLevel.RequiresConfirmation,
                SupportsPreview = true
            }
        ];
    }
}
