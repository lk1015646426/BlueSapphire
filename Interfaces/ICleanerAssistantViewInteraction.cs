using System.Threading.Tasks;

namespace BlueSapphire.Interfaces
{
    public interface ICleanerAssistantViewInteraction
    {
        Task ShowTipAsync(string title, string message);
        Task<bool> ShowCleanupConfirmationAsync(int count, string sizeText, bool includesReviewItems);
        Task<bool> ShowRestoreConfirmationAsync(string summaryText);
        Task<bool> ShowRuleDisableConfirmationAsync(string ruleName, string ruleId);
        Task<string?> PickRulePackFileAsync();
        Task<string?> PromptRulePackUrlAsync(string? currentUrl);
        Task<string?> PromptTelemetryEndpointAsync(string? currentUrl);
    }
}
