using BlueSapphire.Models;
using BlueSapphire.Services;

namespace BlueSapphire.Tests;

public sealed class AITaskCenterServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "BlueSapphire.Tests",
        "AITaskCenter",
        Guid.NewGuid().ToString("N"));

    public AITaskCenterServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Begin_ReportAndCancel_TracksMonotonicProgressAndCancellation()
    {
        using var service = new AITaskCenterService(_root);
        using AITaskLease task = service.Begin("scan", "扫描", "开始", "same-scan");

        service.Report(task.TaskId, 60, "扫描", "60%");
        service.Report(task.TaskId, 30, "乱序进度", "不应倒退");
        Assert.Equal(60, service.Get(task.TaskId)!.Progress);

        using AITaskLease duplicate = service.Begin("scan", "扫描", "重复", "same-scan");
        Assert.True(duplicate.IsDuplicate);
        Assert.Equal(task.TaskId, duplicate.TaskId);

        Assert.True(service.Cancel(task.TaskId));
        Assert.True(task.Token.IsCancellationRequested);
        Assert.Equal(AITaskStatus.Cancelled, service.Get(task.TaskId)!.Status);
    }

    [Fact]
    public async Task Reload_ConvertsUnfinishedTaskToInterrupted()
    {
        string taskId;
        using (var service = new AITaskCenterService(_root))
        {
            using AITaskLease task = service.Begin("media", "媒体分析", "进行中");
            taskId = task.TaskId;
            service.Report(task.TaskId, 42, "分析", "正在校验");
            await service.FlushAsync();
        }

        using var reloaded = new AITaskCenterService(_root);
        AITaskRecord restored = reloaded.Get(taskId)!;

        Assert.Equal(AITaskStatus.Interrupted, restored.Status);
        Assert.Equal(42, restored.Progress);
        Assert.Contains("不会自动续跑", restored.Summary);
    }

    [Fact]
    public void FlushAndDispose_FromUiLikeContext_DoesNotDeadlock()
    {
        using ManualResetEventSlim completed = new(false);
        Exception? failure = null;
        Thread thread = new(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
            try
            {
                var service = new AITaskCenterService(_root);
                for (int index = 0; index < 40; index++)
                {
                    using AITaskLease task = service.Begin(
                        "shutdown-test",
                        $"任务 {index}",
                        new string('x', 800),
                        $"shutdown-{index}");
                    service.Report(task.TaskId, 50, "处理中", new string('y', 800));
                }

                // 模拟 WinUI Closed 事件中的同步退出路径：即使当前上下文不再泵消息，也必须完成。
                service.FlushAsync().GetAwaiter().GetResult();
                service.Dispose();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                completed.Set();
            }
        })
        {
            IsBackground = true
        };

        thread.Start();
        Assert.True(completed.Wait(TimeSpan.FromSeconds(5)), "退出保存不应等待 UI SynchronizationContext。");
        Assert.Null(failure);
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            // 模拟窗口已进入关闭阶段、不再处理派发消息的 UI 上下文。
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
