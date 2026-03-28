using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BlueSapphire.Models
{
    public enum DocumentOperationResultTargetKind
    {
        None,
        File,
        Folder
    }

    public enum DocumentConversionBatchItemStatus
    {
        Succeeded,
        Failed,
        Skipped
    }

    public sealed class DocumentConversionBatchItem
    {
        public string SourcePath { get; }

        public string? OutputPath { get; }

        public DocumentOperationResultTargetKind ResultTargetKind { get; }

        public DocumentConversionBatchItemStatus Status { get; }

        public string Message { get; }

        public string SourceName => Path.GetFileName(SourcePath);

        public string OutputName => string.IsNullOrWhiteSpace(OutputPath) ? "-" : Path.GetFileName(OutputPath);

        public string StatusText => Status switch
        {
            DocumentConversionBatchItemStatus.Succeeded => "成功",
            DocumentConversionBatchItemStatus.Failed => "失败",
            _ => "跳过"
        };

        public string StatusGlyph => Status switch
        {
            DocumentConversionBatchItemStatus.Succeeded => "\uE73E",
            DocumentConversionBatchItemStatus.Failed => "\uEA39",
            _ => "\uE73A"
        };

        public SolidColorBrush StatusBrush => Status switch
        {
            DocumentConversionBatchItemStatus.Succeeded => new(Windows.UI.Color.FromArgb(255, 91, 227, 125)),
            DocumentConversionBatchItemStatus.Failed => new(Windows.UI.Color.FromArgb(255, 255, 107, 107)),
            _ => new(Windows.UI.Color.FromArgb(255, 255, 196, 86))
        };

        public string DetailText => Status == DocumentConversionBatchItemStatus.Succeeded
            ? $"输出：{OutputName}"
            : Message;

        public string PathText => Status == DocumentConversionBatchItemStatus.Succeeded && !string.IsNullOrWhiteSpace(OutputPath)
            ? OutputPath!
            : SourcePath;

        public string ToolTipText => Status == DocumentConversionBatchItemStatus.Succeeded && !string.IsNullOrWhiteSpace(OutputPath)
            ? $"源文件：{SourcePath}\n输出文件：{OutputPath}"
            : $"源文件：{SourcePath}\n说明：{Message}";

        public bool CanOpenResult =>
            Status == DocumentConversionBatchItemStatus.Succeeded &&
            ResultTargetKind != DocumentOperationResultTargetKind.None &&
            !string.IsNullOrWhiteSpace(OutputPath);

        public DocumentConversionBatchItem(
            string sourcePath,
            string? outputPath,
            DocumentConversionBatchItemStatus status,
            string message,
            DocumentOperationResultTargetKind resultTargetKind = DocumentOperationResultTargetKind.None)
        {
            SourcePath = sourcePath;
            OutputPath = outputPath;
            Status = status;
            Message = message;
            ResultTargetKind = resultTargetKind;
        }
    }

    public sealed class DocumentConversionBatchReport
    {
        public string DialogTitle { get; }

        public string OperationName { get; }

        public DateTimeOffset CreatedAt { get; }

        public IReadOnlyList<DocumentConversionBatchItem> Items { get; }

        public int TotalCount => Items.Count;

        public int SuccessCount => Items.Count(item => item.Status == DocumentConversionBatchItemStatus.Succeeded);

        public int FailedCount => Items.Count(item => item.Status == DocumentConversionBatchItemStatus.Failed);

        public int SkippedCount => Items.Count(item => item.Status == DocumentConversionBatchItemStatus.Skipped);

        public string SummaryText => $"成功 {SuccessCount} 个，跳过 {SkippedCount} 个，失败 {FailedCount} 个";

        public string QueueSummaryText => FailedCount > 0
            ? $"{OperationName}：成功 {SuccessCount} / 跳过 {SkippedCount} / 失败 {FailedCount}"
            : $"{OperationName}：成功 {SuccessCount} / 跳过 {SkippedCount}";

        public string CreatedAtText => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        public string HistoryStatusText => FailedCount > 0
            ? "有失败项"
            : SuccessCount > 0
                ? SkippedCount > 0 ? "部分完成" : "已完成"
                : "未执行";

        public string HistoryStatusGlyph => FailedCount > 0
            ? "\uEA39"
            : SuccessCount > 0
                ? "\uE73E"
                : "\uE73A";

        public SolidColorBrush HistoryStatusBrush => FailedCount > 0
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 107, 107))
            : SuccessCount > 0
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 91, 227, 125))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 196, 86));

        public string HistoryCountText => $"{TotalCount} 项";

        public DocumentConversionBatchReport(
            IEnumerable<DocumentConversionBatchItem> items,
            string operationName = "文档处理",
            string dialogTitle = "处理结果",
            DateTimeOffset? createdAt = null)
        {
            Items = items.ToList();
            OperationName = operationName;
            DialogTitle = dialogTitle;
            CreatedAt = createdAt ?? DateTimeOffset.Now;
        }
    }
}
