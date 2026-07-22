using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlueSapphire.Interfaces;
using BlueSapphire.Models;

namespace BlueSapphire.Services;

/// <summary>
/// 媒体管家拥有的 AI 动作。AI 中控只负责注册和调度，不再持有媒体动作实现。
/// </summary>
public sealed class MediaAIToolActionProvider : IAIToolActionProvider
{
    private readonly AIMediaToolService _mediaToolService;
    private readonly AIPrivacyService _privacyService;
    private readonly AISharedContextService _sharedContext;
    private readonly AIOperationPolicyService _operationPolicy;

    public MediaAIToolActionProvider(
        AIMediaToolService mediaToolService,
        AIPrivacyService privacyService,
        AISharedContextService sharedContext,
        AIOperationPolicyService operationPolicy)
    {
        _mediaToolService = mediaToolService;
        _privacyService = privacyService;
        _sharedContext = sharedContext;
        _operationPolicy = operationPolicy;
    }

    public string ToolId => "MediaManager";

    public void RegisterHandlers(AIToolActionHandlerRegistry registry)
    {
        registry.Register("analyze_media_folder", (args, context) => AnalyzeMediaFolderAsync(args, context.CancellationToken));
        registry.Register("preview_media_organization", (args, _) => Task.FromResult(PreviewMediaOrganization(args)));
        registry.Register(
            "execute_exact_duplicate_cleanup",
            (args, context) => ExecuteExactDuplicateCleanupAsync(
                args,
                context.RequestConfirmation,
                context.CancellationToken));
        registry.Register(
            "execute_media_organization",
            (_, context) => ExecuteMediaOrganizationAsync(
                context.RequestConfirmation,
                context.CancellationToken));
    }

    private async Task<string> AnalyzeMediaFolderAsync(string args, CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(args);
            JsonElement root = document.RootElement;
            string folderPath = root.GetProperty("folder_path").GetString() ?? string.Empty;
            bool recursive = !root.TryGetProperty("recursive", out JsonElement recursiveProperty) ||
                             recursiveProperty.GetBoolean();
            AIMediaAnalysisContext result = await _mediaToolService.AnalyzeFolderAsync(
                folderPath,
                recursive,
                cancellationToken);

            return JsonSerializer.Serialize(new
            {
                Folder = _privacyService.DescribePathWithoutIdentity(result.FolderPath),
                result.FileCount,
                TotalSize = CleanerSizeFormatter.Format(result.TotalBytes),
                result.ExactDuplicateGroupCount,
                result.SimilarCandidateGroupCount,
                result.LargeFileCount,
                result.LowResolutionCount,
                result.FormatCounts,
                Safety = "当前只完成分析，没有移动、重命名或删除文件。"
            }, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch (Exception ex)
        {
            return $"媒体分析失败：{_privacyService.RedactForRemoteModel(ex.Message)}";
        }
    }

    private string PreviewMediaOrganization(string args)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(args);
            JsonElement root = document.RootElement;
            string folderPath = root.GetProperty("folder_path").GetString() ?? string.Empty;
            bool recursive = !root.TryGetProperty("recursive", out JsonElement recursiveProperty) ||
                             recursiveProperty.GetBoolean();
            AIMediaOrganizationPreview preview = _mediaToolService.BuildOrganizationPreview(
                folderPath,
                recursive);
            return JsonSerializer.Serialize(new
            {
                Folder = _privacyService.DescribePathWithoutIdentity(preview.FolderPath),
                MoveCount = preview.Moves.Count,
                Examples = preview.Moves.Take(12).Select(move => new
                {
                    Source = _privacyService.DescribePathWithoutIdentity(move.SourcePath),
                    Destination = _privacyService.DescribePathWithoutIdentity(move.DestinationPath),
                    move.Reason
                }),
                Safety = "这只是预览，没有移动或重命名文件。"
            }, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch (Exception ex)
        {
            return $"媒体整理预览失败：{_privacyService.RedactForRemoteModel(ex.Message)}";
        }
    }

    private async Task<string> ExecuteExactDuplicateCleanupAsync(
        string args,
        Func<string, Task<bool>>? requestConfirmation,
        CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(args);
            string strategy = document.RootElement.TryGetProperty("keep_strategy", out JsonElement strategyProperty)
                ? strategyProperty.GetString() ?? "newest"
                : "newest";
            var targets = _mediaToolService.BuildExactDuplicateDeletionPreview(strategy);
            if (targets.Count == 0)
            {
                return "没有 30 分钟内的完全重复图片候选，请先执行媒体目录分析。";
            }

            string fingerprint = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(string.Join(
                    "|",
                    targets.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)))));
            if (!await _operationPolicy.ConfirmAsync(
                requestConfirmation,
                "media.exact-duplicate-cleanup",
                fingerprint,
                $"将把 {targets.Count} 张完全重复图片移入系统回收站。\n保留策略：{(strategy == "oldest" ? "保留最早文件" : "保留最新文件")}\n\n相似但不完全相同的图片不会删除。是否继续？"))
            {
                return "用户已取消重复图片清理。";
            }

            (int success, int failed) = await _mediaToolService.DeleteExactDuplicateCandidatesAsync(
                targets,
                cancellationToken);
            return $"重复图片处理完成：成功移入回收站 {success} 张，失败 {failed} 张。";
        }
        catch (Exception ex)
        {
            return $"重复图片清理失败：{_privacyService.RedactForRemoteModel(ex.Message)}";
        }
    }

    private async Task<string> ExecuteMediaOrganizationAsync(
        Func<string, Task<bool>>? requestConfirmation,
        CancellationToken cancellationToken)
    {
        AIMediaOrganizationPreview? preview =
            _sharedContext.GetMediaOrganizationPreview(TimeSpan.FromMinutes(30));
        if (preview == null || preview.Moves.Count == 0)
        {
            return "没有 30 分钟内的有效媒体整理预览，请先生成预览。";
        }

        if (!await _operationPolicy.ConfirmAsync(
            requestConfirmation,
            "media.organize",
            preview.CreatedAt.ToString("O"),
            $"将按年月移动 {preview.Moves.Count} 张图片。\n不会覆盖同名文件，冲突项将跳过；标签会跟随移动。\n\n是否继续？"))
        {
            return "用户已取消媒体整理。";
        }

        try
        {
            (int success, int failed, int skipped) =
                await _mediaToolService.ExecuteOrganizationPreviewAsync(cancellationToken);
            return $"媒体整理完成：成功 {success} 张，失败 {failed} 张，跳过 {skipped} 张。";
        }
        catch (Exception ex)
        {
            return $"媒体整理失败：{_privacyService.RedactForRemoteModel(ex.Message)}";
        }
    }
}
