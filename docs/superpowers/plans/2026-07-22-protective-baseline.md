# Protective Baseline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** 恢复 BlueSapphire 正式开发日志历史，锁定运行时与源码隔离边界，并建立不会误提交生成物的可验证重构基线。

**Architecture:** 以 `Assets/DevMatrixLog.json` 为只读发布种子，以 `%LocalAppData%/BlueSapphire` 或测试注入目录为唯一运行时写入位置。使用一个针对正式历史和真实文件写入行为的回归测试类保护边界，再通过精确 `.gitignore` 规则隔离安装包生成物，不批量隐藏本地协作配置或核心新源码。

**Tech Stack:** C#、.NET 8、xUnit、System.Text.Json、WinUI 3、PowerShell、Git。

## Global Constraints

- `Assets/DevMatrixLog.json` 只允许追加，禁止删除、覆盖、重排或修改已有正式节点。
- 恢复基线必须逐字段保留正式节点的 `Id`、`Version`、`Title`、`Description` 和 `FullContent`。
- 当前确认的 `seed-100` 和 `seed-101` 是污染数据，必须从源码种子中移除。
- 不改变 Cleaner 扫描、风险、选择、执行、恢复、自动保洁或 AI 决策行为。
- 不执行 `git reset --hard`、`git clean` 或覆盖当前工作区其他未提交改动。
- 每次提交只暂存任务明确列出的文件，并在提交前检查 `git diff --cached --name-only`。
- 全量测试必须不少于 215 项且零失败。
- 构建命令必须是 `dotnet build BlueSapphire.slnx --no-incremental --no-restore -v:minimal`，结果为 0 警告、0 错误。
- 真实 WinUI 验证必须获得非零窗口句柄、`Responding=True`，并通过正常关闭请求以退出码 0 结束。

---

## File Map

- Modify: `Assets/DevMatrixLog.json` — 恢复并承载只追加的正式发布日志种子。
- Modify: `Services/DevLogDataService.cs` — 保留当前运行时只写应用数据目录的实现，移除源码反向同步路径。
- Modify: `BlueSapphire.Tests/DevLogSourceIsolationTests.cs` — 同时验证正式历史不变量和真实保存行为不会修改源码种子。
- Modify: `.gitignore` — 精确忽略根目录 `Output/` 安装包生成物。
- Create: `docs/superpowers/plans/2026-07-22-protective-baseline.md` — 本实施计划。

---

### Task 1: 用失败测试锁定正式历史与源码隔离边界

**Files:**
- Modify: `BlueSapphire.Tests/DevLogSourceIsolationTests.cs`
- Test: `BlueSapphire.Tests/DevLogSourceIsolationTests.cs`

**Interfaces:**
- Consumes: `DevLogDataService(ILogger<DevLogDataService>, string? rootPathOverride, string? seedFilePathOverride)`、`LoadLogsAsync()`、`SaveLogsAsync(List<DevLogItem>)`。
- Produces: 两个回归测试 `SourceSeed_PreservesFormalHistoryAndRejectsKnownPollution` 和 `SaveLogsAsync_WritesRuntimeCopyWithoutChangingSourceSeed`。

- [x] **Step 1: 将源码文本检查升级为历史不变量和行为测试**

用以下完整内容替换 `BlueSapphire.Tests/DevLogSourceIsolationTests.cs`：

```csharp
using BlueSapphire.Models;
using BlueSapphire.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace BlueSapphire.Tests;

public sealed class DevLogSourceIsolationTests
{
    [Fact]
    public void SourceSeed_PreservesFormalHistoryAndRejectsKnownPollution()
    {
        string sourcePath = GetSourceSeedPath();
        List<DevLogItem> logs = JsonSerializer.Deserialize<List<DevLogItem>>(
            File.ReadAllText(sourcePath)) ?? [];

        Assert.NotEmpty(logs);
        DevLogItem formal = logs[0];
        Assert.Equal("a245943f-0512-4f4b-99af-5bd3fdcdaf5e", formal.Id);
        Assert.Equal("1.0.0", formal.Version);
        Assert.Equal("Keep", formal.Title);
        Assert.Equal(string.Empty, formal.Description);
        Assert.Equal(string.Empty, formal.FullContent);
        Assert.DoesNotContain(logs, item => item.Id is "seed-100" or "seed-101");
    }

    [Fact]
    public async Task SaveLogsAsync_WritesRuntimeCopyWithoutChangingSourceSeed()
    {
        string sourcePath = GetSourceSeedPath();
        string sourceBefore = await File.ReadAllTextAsync(sourcePath);
        string testRoot = Path.Combine(
            Path.GetTempPath(),
            "BlueSapphireDevLogIsolationTests",
            Guid.NewGuid().ToString("N"));
        string runtimeRoot = Path.Combine(testRoot, "runtime");
        string seedCopy = Path.Combine(testRoot, "seed.json");

        Directory.CreateDirectory(testRoot);
        await File.WriteAllTextAsync(seedCopy, sourceBefore);

        try
        {
            DevLogDataService service = new(
                NullLogger<DevLogDataService>.Instance,
                runtimeRoot,
                seedCopy);

            List<DevLogItem> runtimeLogs = await service.LoadLogsAsync();
            runtimeLogs.Add(new DevLogItem
            {
                Id = "runtime-only",
                Title = "Runtime only",
                Description = "Must not be copied into project Assets",
                FullContent = string.Empty,
                Version = "runtime-test",
                UpdateLevel = "常规迭代",
                Status = DevLogStatus.Completed,
                Timestamp = new DateTime(2026, 7, 22, 12, 0, 0)
            });

            await service.SaveLogsAsync(runtimeLogs);

            Assert.True(File.Exists(service.DataFilePath));
            Assert.Contains("runtime-only", await File.ReadAllTextAsync(service.DataFilePath));
            Assert.Equal(sourceBefore, await File.ReadAllTextAsync(sourcePath));
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    private static string GetSourceSeedPath()
    {
        return Path.Combine(FindProjectRoot(), "Assets", "DevMatrixLog.json");
    }

    private static string FindProjectRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BlueSapphire.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("未找到 BlueSapphire 项目根目录。");
    }
}
```

- [x] **Step 2: 运行聚焦测试并确认当前污染数据导致失败**

Run:

```powershell
dotnet test BlueSapphire.Tests\BlueSapphire.Tests.csproj --no-restore -v:minimal --filter "FullyQualifiedName~DevLogSourceIsolationTests"
```

Expected: FAIL；`SourceSeed_PreservesFormalHistoryAndRejectsKnownPollution` 在首节点 `Id` 断言处报告实际值为 `seed-100`。行为测试可以通过，但本测试类整体必须失败。

---

### Task 2: 恢复正式种子并完成运行时源码隔离修复

**Files:**
- Modify: `Assets/DevMatrixLog.json`
- Modify: `Services/DevLogDataService.cs`
- Test: `BlueSapphire.Tests/DevLogSourceIsolationTests.cs`
- Test: `BlueSapphire.Tests/DevLogDataServiceTests.cs`

**Interfaces:**
- Consumes: Task 1 创建的两个回归测试。
- Produces: 只包含正式历史节点的源码种子；不存在 `TryGetProjectAssetPath` 或 Debug 源码回写逻辑的 `DevLogDataService`。

- [x] **Step 1: 恢复正式开发日志节点**

将 `Assets/DevMatrixLog.json` 恢复为以下精确内容：

```json
[
  {
    "Id": "a245943f-0512-4f4b-99af-5bd3fdcdaf5e",
    "Title": "Keep",
    "Description": "",
    "FullContent": "",
    "Version": "1.0.0",
    "UpdateLevel": "常规迭代",
    "Status": 2,
    "Timestamp": "2026-03-31T10:00:00"
  }
]
```

- [x] **Step 2: 核对并保留 `DevLogDataService` 的最小隔离实现**

确认 `Services/DevLogDataService.cs` 的 `PersistLogsAsync` 在创建运行时备份后直接结束，不包含以下任何逻辑：

```csharp
#if DEBUG
File.Copy(DataFilePath, projectAssetPath, true);
#endif
```

并确认整个文件不存在：

```csharp
private string? TryGetProjectAssetPath()
```

如果当前工作区已经满足，不重新格式化或改写其他代码，只将现有删除源码回写逻辑的差异纳入本任务。

- [x] **Step 3: 运行源码隔离聚焦测试**

Run:

```powershell
dotnet test BlueSapphire.Tests\BlueSapphire.Tests.csproj --no-restore -v:minimal --filter "FullyQualifiedName~DevLogSourceIsolationTests"
```

Expected: PASS，2 项通过、0 项失败。

- [x] **Step 4: 运行开发日志服务邻近测试**

Run:

```powershell
dotnet test BlueSapphire.Tests\BlueSapphire.Tests.csproj --no-restore -v:minimal --filter "FullyQualifiedName~DevLogDataServiceTests|FullyQualifiedName~DevLogSourceIsolationTests"
```

Expected: PASS，开发日志服务和源码隔离测试全部通过。

- [x] **Step 5: 验证源码种子只发生允许的恢复差异**

Run:

```powershell
git diff -- Assets/DevMatrixLog.json Services/DevLogDataService.cs BlueSapphire.Tests/DevLogSourceIsolationTests.cs
git diff --check -- Assets/DevMatrixLog.json Services/DevLogDataService.cs BlueSapphire.Tests/DevLogSourceIsolationTests.cs
```

Expected: JSON 从两个污染节点恢复为原正式节点；服务差异只删除 Debug 反写源码逻辑；测试新增两个明确回归场景；`git diff --check` 无输出。

- [x] **Step 6: 创建开发日志保护检查点**

Run:

```powershell
git add -- Assets/DevMatrixLog.json Services/DevLogDataService.cs BlueSapphire.Tests/DevLogSourceIsolationTests.cs
git diff --cached --name-only
git commit -m "fix: 保护开发日志正式历史"
```

Expected staged files exactly（`Assets/DevMatrixLog.json` 恢复后与 `HEAD` 内容相同，只刷新工作区状态，不产生提交差异）:

```text
BlueSapphire.Tests/DevLogSourceIsolationTests.cs
Services/DevLogDataService.cs
```

---

### Task 3: 精确隔离安装包生成物并盘点剩余工作区

**Files:**
- Modify: `.gitignore`

**Interfaces:**
- Consumes: Git 工作区当前未跟踪文件列表。
- Produces: 根目录 `Output/` 被忽略；核心 `Services/*.cs`、`BlueSapphire.Tests/*.cs`、`.agents/AGENTS.md` 不被宽泛规则隐藏。

- [x] **Step 1: 证明安装包当前未被忽略**

Run:

```powershell
git check-ignore -q Output/BlueSapphire_Setup_v1.0.3.exe
if ($LASTEXITCODE -eq 0) { throw "预期 Output 当前未被忽略" }
```

Expected: 命令正常完成，证明修复前安装包仍作为未跟踪生成物出现。

- [x] **Step 2: 添加根目录精确忽略规则**

在 `.gitignore` 末尾添加：

```gitignore

# Local installer build output
/Output/
```

不得添加 `*.exe`，因为宽泛扩展名规则可能隐藏未来需要审查的工具或测试资产。不得忽略 `.agents/`、`.trae/`、`.workbuddy/` 或 `Services/`。

- [x] **Step 3: 验证只隔离生成物**

Run:

```powershell
git check-ignore -v Output/BlueSapphire_Setup_v1.0.3.exe
$protected = @(
    'Services/CleanerOperationCoordinator.cs',
    'Services/AIToolCapabilityCatalog.cs',
    'BlueSapphire.Tests/CleanerOperationCoordinatorTests.cs',
    '.agents/AGENTS.md'
)
foreach ($path in $protected) {
    git check-ignore -q -- $path
    if ($LASTEXITCODE -eq 0) { throw "核心文件被错误忽略: $path" }
}
```

Expected: 第一条命中 `/Output/`；四个受保护路径均未被忽略。

- [x] **Step 4: 输出剩余未跟踪文件分类清单**

Run:

```powershell
$untracked = @(git ls-files --others --exclude-standard)
[PSCustomObject]@{
    CoreSourceAndTests = @($untracked | Where-Object { $_ -match '^(Services|Interfaces|Models|Helpers|BlueSapphire.Tests)/' }).Count
    ProjectDocs = @($untracked | Where-Object { $_ -match '^(docs|DESIGN|UI_REDESIGN)' }).Count
    CollaborationConfig = @($untracked | Where-Object { $_ -match '^\.(agents|trae|workbuddy)/' }).Count
    Other = @($untracked | Where-Object { $_ -notmatch '^(Services|Interfaces|Models|Helpers|BlueSapphire.Tests|docs)/' -and $_ -notmatch '^(DESIGN|UI_REDESIGN)' -and $_ -notmatch '^\.(agents|trae|workbuddy)/' }).Count
} | Format-List
```

Expected: `Output/*.exe` 不再出现在 `$untracked`；核心源码与测试仍可见，不删除任何未跟踪文件。

- [x] **Step 5: 创建 Git 卫生检查点**

Run:

```powershell
git add -- .gitignore
git diff --cached --name-only
git diff --cached --check
git commit -m "chore: 忽略本地安装包生成物"
```

Expected staged files exactly:

```text
.gitignore
```

---

### Task 4: 完成全量验证并确认测试过程不污染源码

**Files:**
- Verify only: `Assets/DevMatrixLog.json`
- Verify only: `BlueSapphire.slnx`
- Verify only: `bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64/BlueSapphire.exe`

**Interfaces:**
- Consumes: Tasks 1–3 的两个 Git 检查点。
- Produces: 当前阶段的测试、构建、真实窗口、正常关闭和源码未污染证据。

- [x] **Step 1: 记录测试前源码种子哈希**

Run:

```powershell
$sourceSeed = Resolve-Path 'Assets\DevMatrixLog.json'
$beforeHash = (Get-FileHash -Algorithm SHA256 $sourceSeed).Hash
Write-Host "Before=$beforeHash"
```

Expected: 输出一个非空 SHA-256 哈希。

- [x] **Step 2: 运行全量测试**

Run:

```powershell
dotnet test BlueSapphire.Tests\BlueSapphire.Tests.csproj --no-restore -v:minimal
```

Expected: 至少 215 项通过、0 项失败、0 项跳过。

- [x] **Step 3: 证明测试没有污染源码种子**

Run in the same PowerShell session:

```powershell
$afterHash = (Get-FileHash -Algorithm SHA256 $sourceSeed).Hash
if ($beforeHash -ne $afterHash) {
    throw "全量测试修改了 Assets/DevMatrixLog.json"
}
Write-Host "After=$afterHash"
```

Expected: `Before` 与 `After` 完全相同。

- [x] **Step 4: 运行无增量零警告构建**

Run:

```powershell
dotnet build BlueSapphire.slnx --no-incremental --no-restore -v:minimal
```

Expected: `已成功生成`、0 个警告、0 个错误。

- [x] **Step 5: 启动并正常关闭真实 Cleaner 窗口**

Run:

```powershell
$exe = Resolve-Path 'bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\BlueSapphire.exe'
$process = Start-Process -FilePath $exe -ArgumentList '--tool=CleanerAssistant' -PassThru
$deadline = (Get-Date).AddSeconds(30)
$ready = $false

do {
    Start-Sleep -Milliseconds 500
    $process.Refresh()
    if ($process.HasExited) { break }
    if ($process.MainWindowHandle -ne 0 -and $process.Responding) {
        $ready = $true
        break
    }
} while ((Get-Date) -lt $deadline)

if (-not $ready) {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    throw "Cleaner 窗口未在 30 秒内进入可响应状态"
}

Write-Host "Handle=$($process.MainWindowHandle) Responding=$($process.Responding)"
if (-not $process.CloseMainWindow()) {
    Stop-Process -Id $process.Id -Force
    throw "无法发送正常关闭请求"
}
if (-not $process.WaitForExit(15000)) {
    Stop-Process -Id $process.Id -Force
    throw "正常关闭请求后 15 秒内未退出"
}
if ($process.ExitCode -ne 0) {
    throw "应用退出码异常: $($process.ExitCode)"
}
```

Expected: 非零 `Handle`、`Responding=True`，正常退出且 `ExitCode=0`。

- [x] **Step 6: 最终工作区边界核对**

Run:

```powershell
git status --short --branch
git log -3 --oneline --decorate
git diff --check
```

Expected:

- 最近提交依次包含设计文档、开发日志保护、安装包忽略规则。
- `Assets/DevMatrixLog.json` 不再是未提交修改。
- `Output/` 不再出现在未跟踪列表。
- Cleaner、AI、UI 的既有未提交重构仍然保留。
- 对本阶段四个文件执行的 `git diff --check` 无输出；其他重构文件的既有空白问题留待所属阶段处理。
---

## Execution Result (2026-07-22)

- RED：源码种子仍含污染节点时，`DevLogSourceIsolationTests` 结果为 1 失败、1 通过，失败实际值为 `seed-100`。
- GREEN：恢复正式种子后，源码隔离聚焦测试 2/2 通过，邻近开发日志测试 6/6 通过。
- 全量回归：216/216 通过，0 失败，0 跳过。
- 源码种子保护：全量测试前后 SHA-256 均为 `A9460CC2C03CFED65B36BE74E91D5CD2FBACD978D4243F8E6EFB9C81EBB64FEC`。
- 无增量构建：0 警告、0 错误。
- 真实窗口：非零句柄 `397254`、`Responding=True`，正常关闭成功，退出码 0。
- Git 卫生：`Output/` 已由根目录精确规则忽略；核心未跟踪源码、测试和 `.agents/AGENTS.md` 仍保持可见。
- 当前开发机运行时副本：发现并备份后移除残留的 seed-101，正式节点恢复为源种子的原始 Id；再次启动应用后源码和运行时哈希均未变化。
- 运行时备份：C:\Users\10156\AppData\Local\BlueSapphire\LogBackups\DevMatrixLog_pre_protective_baseline_20260722_171622.json。