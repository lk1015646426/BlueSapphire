using BlueSapphire.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.Services
{
    public sealed class AITaskCenterService : IDisposable
    {
        private const int MaxTasks = 80;
        private const int MaxTimelineEntries = 120;
        private const long MaxStateBytes = 2 * 1024 * 1024;
        private readonly object _sync = new();
        private readonly string _statePath;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = new(StringComparer.Ordinal);
        private readonly SemaphoreSlim _saveGate = new(1, 1);
        private readonly List<AITaskRecord> _tasks;
        private int _saveScheduled;
        private bool _disposed;

        public event EventHandler? TasksChanged;

        public AITaskCenterService(string? rootPath = null)
        {
            string root = rootPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BlueSapphire");
            Directory.CreateDirectory(root);
            _statePath = Path.Combine(root, "ai_tasks.json");
            _tasks = LoadState();
            RecoverInterruptedTasks();
        }

        public AITaskLease Begin(
            string kind,
            string title,
            string summary,
            string? idempotencyKey = null)
        {
            ThrowIfDisposed();
            string normalizedKey = (idempotencyKey ?? string.Empty).Trim();

            lock (_sync)
            {
                if (normalizedKey.Length > 0)
                {
                    AITaskRecord? existing = _tasks
                        .Where(task => string.Equals(task.IdempotencyKey, normalizedKey, StringComparison.Ordinal))
                        .OrderByDescending(task => task.UpdatedAt)
                        .FirstOrDefault(task =>
                            task.IsActive ||
                            (task.Status == AITaskStatus.Completed &&
                             DateTimeOffset.Now - task.UpdatedAt < TimeSpan.FromMinutes(10)));

                    if (existing != null)
                    {
                        return new AITaskLease(this, existing.Id, CancellationToken.None, isDuplicate: true);
                    }
                }

                var record = new AITaskRecord
                {
                    Kind = Limit(kind, 80),
                    Title = Limit(title, 160),
                    Summary = Limit(summary, 1000),
                    IdempotencyKey = Limit(normalizedKey, 256),
                    Status = AITaskStatus.Running,
                    Progress = 0,
                    CanCancel = true
                };
                AddTimeline(record, AITaskStatus.Running, "任务已开始", record.Summary, 0);
                _tasks.Insert(0, record);
                TrimTasks();

                var cts = new CancellationTokenSource();
                _cancellations[record.Id] = cts;
                NotifyChanged();
                return new AITaskLease(this, record.Id, cts.Token, isDuplicate: false);
            }
        }

        public IReadOnlyList<AITaskRecord> GetSnapshot()
        {
            lock (_sync)
            {
                return _tasks
                    .Select(Clone)
                    .ToList();
            }
        }

        public AITaskRecord? Get(string taskId)
        {
            lock (_sync)
            {
                AITaskRecord? task = _tasks.FirstOrDefault(item => item.Id == taskId);
                return task == null ? null : Clone(task);
            }
        }

        public void Report(
            string taskId,
            double progress,
            string title,
            string detail,
            AITaskStatus status = AITaskStatus.Running)
        {
            lock (_sync)
            {
                AITaskRecord? task = Find(taskId);
                if (task == null || !task.IsActive)
                {
                    return;
                }

                task.Status = status;
                task.Progress = Math.Max(task.Progress, Math.Clamp(progress, 0, 100));
                task.Summary = Limit(detail, 1000);
                task.UpdatedAt = DateTimeOffset.Now;
                AddTimeline(task, status, title, detail, task.Progress);
            }
            NotifyChanged();
        }

        public void Complete(string taskId, string summary)
        {
            Finish(taskId, AITaskStatus.Completed, summary, 100);
        }

        public void Fail(string taskId, string summary)
        {
            Finish(taskId, AITaskStatus.Failed, summary, null);
        }

        public void MarkCancelled(string taskId, string summary = "任务已由用户取消")
        {
            Finish(taskId, AITaskStatus.Cancelled, summary, null);
        }

        public bool Cancel(string taskId)
        {
            if (!_cancellations.TryGetValue(taskId, out CancellationTokenSource? cts))
            {
                return false;
            }

            try
            {
                cts.Cancel();
                MarkCancelled(taskId);
                return true;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        public void RemoveCompleted()
        {
            lock (_sync)
            {
                _tasks.RemoveAll(task => !task.IsActive);
            }
            NotifyChanged();
        }

        public Task FlushAsync() => SaveAsync();

        internal void Release(string taskId)
        {
            if (_cancellations.TryRemove(taskId, out CancellationTokenSource? cts))
            {
                cts.Dispose();
            }
        }

        private void Finish(string taskId, AITaskStatus status, string summary, double? progress)
        {
            lock (_sync)
            {
                AITaskRecord? task = Find(taskId);
                if (task == null)
                {
                    return;
                }

                task.Status = status;
                task.CanCancel = false;
                task.Progress = progress ?? task.Progress;
                task.Summary = Limit(summary, 1000);
                task.UpdatedAt = DateTimeOffset.Now;
                task.CompletedAt = task.UpdatedAt;
                AddTimeline(task, status, task.StatusText, summary, task.Progress);
            }
            Release(taskId);
            NotifyChanged();
        }

        private AITaskRecord? Find(string taskId) =>
            _tasks.FirstOrDefault(task => string.Equals(task.Id, taskId, StringComparison.Ordinal));

        private void NotifyChanged()
        {
            TasksChanged?.Invoke(this, EventArgs.Empty);
            ScheduleSave();
        }

        private void ScheduleSave()
        {
            if (Interlocked.Exchange(ref _saveScheduled, 1) != 0)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(120);
                    if (!_disposed)
                    {
                        await SaveAsync();
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _saveScheduled, 0);
                }
            });
        }

        private async Task SaveAsync()
        {
            AITaskSnapshot snapshot;
            lock (_sync)
            {
                snapshot = new AITaskSnapshot
                {
                    Tasks = _tasks.Select(Clone).ToList()
                };
            }

            await _saveGate.WaitAsync().ConfigureAwait(false);
            try
            {
                string tempPath = _statePath + ".tmp";
                byte[] data = JsonSerializer.SerializeToUtf8Bytes(snapshot);
                if (data.LongLength > MaxStateBytes)
                {
                    return;
                }
                await File.WriteAllBytesAsync(tempPath, data).ConfigureAwait(false);
                File.Move(tempPath, _statePath, true);
            }
            catch
            {
            }
            finally
            {
                _saveGate.Release();
            }
        }

        private List<AITaskRecord> LoadState()
        {
            try
            {
                if (!File.Exists(_statePath) ||
                    new FileInfo(_statePath).Length is <= 0 or > MaxStateBytes)
                {
                    return new List<AITaskRecord>();
                }

                AITaskSnapshot? snapshot = JsonSerializer.Deserialize<AITaskSnapshot>(
                    File.ReadAllBytes(_statePath));
                return snapshot?.Tasks
                    .Where(task => !string.IsNullOrWhiteSpace(task.Id))
                    .OrderByDescending(task => task.UpdatedAt)
                    .Take(MaxTasks)
                    .ToList()
                    ?? new List<AITaskRecord>();
            }
            catch
            {
                return new List<AITaskRecord>();
            }
        }

        private void RecoverInterruptedTasks()
        {
            bool changed = false;
            lock (_sync)
            {
                foreach (AITaskRecord task in _tasks.Where(task => task.IsActive))
                {
                    task.Status = AITaskStatus.Interrupted;
                    task.CanCancel = false;
                    task.UpdatedAt = DateTimeOffset.Now;
                    task.CompletedAt = task.UpdatedAt;
                    task.Summary = "应用上次退出时任务尚未完成。涉及写入或删除的步骤不会自动续跑，请重新确认。";
                    AddTimeline(task, task.Status, "任务被中断", task.Summary, task.Progress);
                    changed = true;
                }
            }

            if (changed)
            {
                ScheduleSave();
            }
        }

        private void TrimTasks()
        {
            if (_tasks.Count > MaxTasks)
            {
                _tasks.RemoveRange(MaxTasks, _tasks.Count - MaxTasks);
            }
        }

        private static void AddTimeline(
            AITaskRecord task,
            AITaskStatus status,
            string title,
            string detail,
            double progress)
        {
            task.Timeline.Add(new AITaskTimelineEntry
            {
                Status = status,
                Title = Limit(title, 160),
                Detail = Limit(detail, 1000),
                Progress = Math.Clamp(progress, 0, 100)
            });
            if (task.Timeline.Count > MaxTimelineEntries)
            {
                task.Timeline.RemoveRange(0, task.Timeline.Count - MaxTimelineEntries);
            }
        }

        private static AITaskRecord Clone(AITaskRecord source)
        {
            return new AITaskRecord
            {
                Id = source.Id,
                Kind = source.Kind,
                Title = source.Title,
                Summary = source.Summary,
                IdempotencyKey = source.IdempotencyKey,
                Status = source.Status,
                Progress = source.Progress,
                CanCancel = source.CanCancel,
                CreatedAt = source.CreatedAt,
                UpdatedAt = source.UpdatedAt,
                CompletedAt = source.CompletedAt,
                Timeline = source.Timeline.Select(entry => new AITaskTimelineEntry
                {
                    Timestamp = entry.Timestamp,
                    Status = entry.Status,
                    Title = entry.Title,
                    Detail = entry.Detail,
                    Progress = entry.Progress
                }).ToList()
            };
        }

        private static string Limit(string? value, int maxLength)
        {
            string normalized = (value ?? string.Empty).Trim();
            return normalized[..Math.Min(normalized.Length, maxLength)];
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            // 先阻止延迟保存任务继续进入，再完成最后一次落盘。
            // SaveAsync 内部不捕获 UI SynchronizationContext，避免窗口关闭线程同步等待时死锁。
            _disposed = true;
            foreach (CancellationTokenSource cts in _cancellations.Values)
            {
                try { cts.Cancel(); } catch { }
                cts.Dispose();
            }
            _cancellations.Clear();
            try { SaveAsync().ConfigureAwait(false).GetAwaiter().GetResult(); } catch { }
            _saveGate.Dispose();
        }
    }

    public sealed class AITaskLease : IDisposable
    {
        private readonly AITaskCenterService _owner;
        private bool _disposed;

        internal AITaskLease(
            AITaskCenterService owner,
            string taskId,
            CancellationToken token,
            bool isDuplicate)
        {
            _owner = owner;
            TaskId = taskId;
            Token = token;
            IsDuplicate = isDuplicate;
        }

        public string TaskId { get; }
        public CancellationToken Token { get; }
        public bool IsDuplicate { get; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (!IsDuplicate)
            {
                _owner.Release(TaskId);
            }
        }
    }
}
