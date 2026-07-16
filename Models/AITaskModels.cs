using System;
using System.Collections.Generic;
using System.Linq;

namespace BlueSapphire.Models
{
    public enum AITaskStatus
    {
        Pending,
        Running,
        AwaitingConfirmation,
        Completed,
        Failed,
        Cancelled,
        Interrupted
    }

    public sealed class AITaskTimelineEntry
    {
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
        public AITaskStatus Status { get; set; } = AITaskStatus.Pending;
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public double Progress { get; set; }
    }

    public sealed class AITaskRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Kind { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;
        public AITaskStatus Status { get; set; } = AITaskStatus.Pending;
        public double Progress { get; set; }
        public bool CanCancel { get; set; } = true;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
        public DateTimeOffset? CompletedAt { get; set; }
        public List<AITaskTimelineEntry> Timeline { get; set; } = new();

        public bool IsActive =>
            Status is AITaskStatus.Pending or AITaskStatus.Running or AITaskStatus.AwaitingConfirmation;

        public string StatusText => Status switch
        {
            AITaskStatus.Pending => "等待开始",
            AITaskStatus.Running => "执行中",
            AITaskStatus.AwaitingConfirmation => "等待确认",
            AITaskStatus.Completed => "已完成",
            AITaskStatus.Failed => "失败",
            AITaskStatus.Cancelled => "已取消",
            AITaskStatus.Interrupted => "已中断",
            _ => "未知"
        };

        public string ProgressText => $"{Math.Clamp((int)Math.Round(Progress), 0, 100)}%";
        public string UpdatedAtText => UpdatedAt.ToLocalTime().ToString("MM-dd HH:mm:ss");
        public bool CanCancelNow => IsActive && CanCancel;
        public string TimelineSummaryText => string.Join(
            Environment.NewLine,
            Timeline.TakeLast(4).Select(entry =>
                $"{entry.Timestamp.ToLocalTime():HH:mm:ss} · {entry.Title} · {entry.Detail}"));
    }

    public sealed class AITaskSnapshot
    {
        public int Version { get; set; } = 1;
        public List<AITaskRecord> Tasks { get; set; } = new();
    }

    public sealed class AIMediaAnalysisContext
    {
        public string FolderPath { get; init; } = string.Empty;
        public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
        public int FileCount { get; init; }
        public long TotalBytes { get; init; }
        public int ExactDuplicateGroupCount { get; init; }
        public int SimilarCandidateGroupCount { get; init; }
        public int LargeFileCount { get; init; }
        public int LowResolutionCount { get; init; }
        public Dictionary<string, int> FormatCounts { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public List<List<string>> ExactDuplicateGroups { get; init; } = new();
        public List<List<string>> SimilarCandidateGroups { get; init; } = new();
    }

    public sealed class AIMediaOrganizationPreview
    {
        public string FolderPath { get; init; } = string.Empty;
        public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
        public List<AIMediaMovePreview> Moves { get; init; } = new();
    }

    public sealed class AIMediaMovePreview
    {
        public string SourcePath { get; init; } = string.Empty;
        public string DestinationPath { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
    }
}
