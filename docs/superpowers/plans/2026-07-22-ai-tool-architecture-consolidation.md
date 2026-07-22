# AI Tool Architecture Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将模型工具能力、动作执行、领域 Provider、任务中心和 AI 工作台整理为依赖闭合、可独立构建、可回滚的第三阶段提交。

**Architecture:** 先提交纯契约与目录，再提交领域 Provider 和 `AIToolsRegistry` 编排，随后提交任务中心与最小 App DI，最后提交 AI 工作台和旧入口删除。每个候选切片使用短路径临时工作树，只应用 staged patch 后执行 restore、AI 聚焦测试和无增量构建。混合文件通过临时备份、从 `HEAD` 生成最小候选、暂存后恢复用户工作树内容的方式处理。

**Tech Stack:** C#、.NET 8、WinUI 3、xUnit、PowerShell 7、Git worktree、Microsoft.Extensions.DependencyInjection。

## Global Constraints

- `Assets/DevMatrixLog.json` 全阶段保持 SHA-256 `A9460CC2C03CFED65B36BE74E91D5CD2FBACD978D4243F8E6EFB9C81EBB64FEC`。
- 不执行 `git add .`、`git reset --hard`、`git clean` 或覆盖未提交的全局 UI/主题重构。
- AI 不得改变 Cleaner 风险、范围、执行方式、确认或共享操作门禁。
- Media AI 不得永久删除文件、删除相似图片或覆盖归档目标。
- 未知动作必须返回未识别结果，不执行反射或字符串命令。
- 每个候选提交必须在短路径临时工作树中独立 restore、测试和构建。
- AI 聚焦测试不少于当前 37 项；完整工作树全量测试不少于当前 217 项；均零失败。
- 已提交 AI 快照必须在隔离工作树完成全量测试和 0 警告、0 错误构建。
- 真实 AI 工作台窗口必须获得非零句柄、`Responding=True` 并正常关闭。

---

## File Map

### Verification tooling

- Create: `scripts/verify-ai-slice.ps1`

### Slice A — contracts, capability catalog and action dispatch

- Create: `Interfaces/IAIToolActionProvider.cs`
- Create: `Interfaces/IAIToolCapabilityProvider.cs`
- Create: `Models/AIToolCapabilityModels.cs`
- Create: `Services/AIToolActionHandlerRegistry.cs`
- Create: `Services/AIToolCapabilityCatalog.cs`
- Create: `BlueSapphire.Tests/AIToolActionHandlerRegistryTests.cs`
- Create: `BlueSapphire.Tests/AIToolCapabilityCatalogTests.cs`

### Slice B — domain providers and registry orchestration

- Create: `Services/CleanerAIToolActionProvider.cs`
- Create: `Services/MediaAIToolActionProvider.cs`
- Modify: `Tools/CleanerAssistantTool.cs`
- Modify: `Tools/MediaManagerTool.cs`
- Modify: `Services/AIToolsRegistry.cs`
- Modify: `Services/AIClassifierService.cs`
- Modify: `Services/AIOfflineIntentService.cs`
- Modify: `Services/AISharedContextService.cs`
- Create: `BlueSapphire.Tests/CleanerAIToolActionProviderTests.cs`
- Create: `BlueSapphire.Tests/MediaAIToolActionProviderTests.cs`

### Slice C — task center and minimum App registrations

- Modify: `Services/AITaskCenterService.cs`
- Modify: `BlueSapphire.Tests/AITaskCenterServiceTests.cs`
- Modify: `Models/AppMessages.cs`
- Stage AI-only changes from `App.xaml.cs`.

### Slice D — unified AI workspace and legacy removal

- Modify: `HomePage.xaml`
- Modify: `HomePage.xaml.cs`
- Modify: `Views/AICopilotPage.xaml`
- Modify: `Views/AICopilotPage.xaml.cs`
- Modify: `Tools/HomeTool.cs`
- Delete: `Tools/AICopilotTool.cs`
- Delete: `Tools/AITaskCenterTool.cs`
- Delete: `Views/AITaskCenterPage.xaml`
- Delete: `Views/AITaskCenterPage.xaml.cs`
- Add only the minimum theme compatibility resources proven necessary by XAML closure.

### Evidence

- Modify: `docs/ai-assistant-architecture.md`
- Create: `docs/ai-tool-workflow-acceptance.md`
- Modify: this implementation plan with actual results.

---

### Task 1: Add repeatable AI staged-slice verification

**Files:**
- Create: `scripts/verify-ai-slice.ps1`

**Interfaces:**
- Consumes: current staged patch.
- Produces: exit code 0 only when patch application, restore, selected tests, build and worktree cleanup all succeed.

- [ ] **Step 1: Create the verifier**

Create `scripts/verify-ai-slice.ps1`:

```powershell
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9._-]+$')]
    [string]$Name,
    [string]$TestFilter = 'FullyQualifiedName~AI'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = (& git rev-parse --show-toplevel).Trim()
if (-not $repoRoot) { throw '无法解析 Git 仓库根目录。' }

& git diff --cached --quiet
if ($LASTEXITCODE -eq 0) { throw 'Git 暂存区为空，无法验证候选切片。' }

$tempRoot = Join-Path ([IO.Path]::GetPathRoot($repoRoot)) 'bsw'
$shortName = $Name.Substring(0, [Math]::Min(16, $Name.Length))
$runId = $shortName + '_' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$worktreePath = Join-Path $tempRoot $runId
$patchPath = Join-Path $tempRoot ($runId + '.patch')
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

& git diff --cached --binary --output=$patchPath
if ($LASTEXITCODE -ne 0) { throw '无法生成 staged patch。' }

try {
    & git worktree add --detach $worktreePath HEAD
    if ($LASTEXITCODE -ne 0) { throw '无法创建临时工作树。' }
    & git -C $worktreePath apply --index --binary $patchPath
    if ($LASTEXITCODE -ne 0) { throw '候选补丁无法应用到当前 HEAD。' }
    & dotnet restore (Join-Path $worktreePath 'BlueSapphire.slnx') -v:minimal
    if ($LASTEXITCODE -ne 0) { throw '临时工作树 restore 失败。' }
    $testProject = Join-Path $worktreePath 'BlueSapphire.Tests\BlueSapphire.Tests.csproj'
    if ([string]::IsNullOrWhiteSpace($TestFilter)) {
        & dotnet test $testProject --no-restore -v:minimal
    }
    else {
        & dotnet test $testProject --no-restore -v:minimal --filter $TestFilter
    }
    if ($LASTEXITCODE -ne 0) { throw '临时工作树测试失败。' }
    & dotnet build (Join-Path $worktreePath 'BlueSapphire.slnx') --no-incremental --no-restore -v:minimal
    if ($LASTEXITCODE -ne 0) { throw '临时工作树无增量构建失败。' }
}
finally {
    if (Test-Path -LiteralPath $worktreePath) {
        & git worktree remove --force $worktreePath
        if ($LASTEXITCODE -ne 0) { throw "无法清理临时工作树: $worktreePath" }
    }
    & git worktree prune
    if (Test-Path -LiteralPath $patchPath) { Remove-Item -LiteralPath $patchPath -Force }
}
```

- [ ] **Step 2: Verify empty-index rejection and syntax**

```powershell
$caught = $null
try { .\scripts\verify-ai-slice.ps1 -Name empty-index } catch { $caught = $_.Exception.Message }
if ($caught -notmatch 'Git 暂存区为空') { throw '未按预期拒绝空暂存区' }
$tokens = $null
$errors = $null
[Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path '.\scripts\verify-ai-slice.ps1'),
    [ref]$tokens,
    [ref]$errors) | Out-Null
if ($errors.Count -gt 0) { $errors; throw 'PowerShell 语法检查失败' }
```

- [ ] **Step 3: Commit the verifier**

```powershell
git add -- scripts/verify-ai-slice.ps1
git diff --cached --check
git commit -m "test: 添加 AI 切片隔离验证"
```

---

### Task 2: Consolidate capability and action contracts

**Files:** Slice A files.

**Interfaces:** Produces stable capability snapshots and exact action-name dispatch without domain dependencies.

- [ ] **Step 1: Run current contract tests**

```powershell
dotnet test BlueSapphire.Tests\BlueSapphire.Tests.csproj --no-restore -v:minimal --filter "FullyQualifiedName~AIToolActionHandlerRegistryTests|FullyQualifiedName~AIToolCapabilityCatalogTests"
```

Expected: 7 tests pass.

- [ ] **Step 2: Stage Slice A**

```powershell
$sliceA = @(
 'Interfaces/IAIToolActionProvider.cs',
 'Interfaces/IAIToolCapabilityProvider.cs',
 'Models/AIToolCapabilityModels.cs',
 'Services/AIToolActionHandlerRegistry.cs',
 'Services/AIToolCapabilityCatalog.cs',
 'BlueSapphire.Tests/AIToolActionHandlerRegistryTests.cs',
 'BlueSapphire.Tests/AIToolCapabilityCatalogTests.cs'
)
git add -- $sliceA
git diff --cached --check
```

- [ ] **Step 3: Isolate, test and build**

```powershell
.\scripts\verify-ai-slice.ps1 -Name ai-contracts -TestFilter "FullyQualifiedName~AIToolActionHandlerRegistryTests|FullyQualifiedName~AIToolCapabilityCatalogTests"
```

Expected: contract tests and build pass independently.

- [ ] **Step 4: Commit Slice A**

```powershell
git commit -m "feat: 建立 AI 工具能力与动作契约"
```

---

### Task 3: Consolidate Cleaner/Media providers and slim the registry

**Files:** Slice B files.

**Interfaces:** Consumes Slice A contracts; produces domain-owned action handlers and an orchestration-only `AIToolsRegistry`.

- [ ] **Step 1: Run provider and registry-related tests**

```powershell
dotnet test BlueSapphire.Tests\BlueSapphire.Tests.csproj --no-restore -v:minimal --filter "FullyQualifiedName~CleanerAIToolActionProviderTests|FullyQualifiedName~MediaAIToolActionProviderTests|FullyQualifiedName~AITool"
```

Expected: all selected tests pass.

- [ ] **Step 2: Verify domain actions left the registry**

```powershell
$registry = Get-Content Services\AIToolsRegistry.cs -Raw
$forbidden = @(
 'StartSmartCleanupAsync',
 'ExecuteCleanupAsync',
 'AnalyzeMediaFolderAsync',
 'ExecuteExactDuplicateCleanupAsync',
 'ExecuteMediaOrganizationAsync'
)
foreach ($name in $forbidden) {
    if ($registry.Contains($name)) { throw "AIToolsRegistry 仍包含领域实现: $name" }
}
```

- [ ] **Step 3: Stage Slice B**

```powershell
$sliceB = @(
 'Services/CleanerAIToolActionProvider.cs',
 'Services/MediaAIToolActionProvider.cs',
 'Tools/CleanerAssistantTool.cs',
 'Tools/MediaManagerTool.cs',
 'Services/AIToolsRegistry.cs',
 'Services/AIClassifierService.cs',
 'Services/AIOfflineIntentService.cs',
 'Services/AISharedContextService.cs',
 'BlueSapphire.Tests/CleanerAIToolActionProviderTests.cs',
 'BlueSapphire.Tests/MediaAIToolActionProviderTests.cs'
)
git add -- $sliceB
git diff --cached --check
```

- [ ] **Step 4: Isolate and commit**

```powershell
.\scripts\verify-ai-slice.ps1 -Name ai-providers -TestFilter "FullyQualifiedName~CleanerAIToolActionProviderTests|FullyQualifiedName~MediaAIToolActionProviderTests|FullyQualifiedName~AITool"
git commit -m "feat: 拆分 Cleaner 与 Media AI 动作 Provider"
```

If compile errors prove `AIToolsRegistry` requires App DI or task-center changes, merge only Slice C. Do not add Home/UI files to a service dependency closure.

---

### Task 4: Consolidate task center and AI-only App registrations

**Files:** Slice C files.

**Interfaces:** Consumes provider/catalog types; produces DI-complete singleton orchestration and restart-safe task state.

- [ ] **Step 1: Run task-center tests**

```powershell
dotnet test BlueSapphire.Tests\BlueSapphire.Tests.csproj --no-restore -v:minimal --filter "FullyQualifiedName~AITaskCenterServiceTests"
```

- [ ] **Step 2: Prepare AI-only `App.xaml.cs` candidate**

Use a temporary backup because the main worktree contains unrelated App/theme changes. Starting from `HEAD:App.xaml.cs`, apply these exact transformations:

1. Replace old transient tool registrations and old AI tool registrations with:

```csharp
services.AddTransient<HomeTool>();
services.AddSingleton<MediaManagerTool>();
services.AddSingleton<CleanerAssistantTool>();
services.AddSingleton<BlueSapphire.Interfaces.IAIToolCapabilityProvider>(sp => sp.GetRequiredService<MediaManagerTool>());
services.AddSingleton<BlueSapphire.Interfaces.IAIToolCapabilityProvider>(sp => sp.GetRequiredService<CleanerAssistantTool>());
```

2. Add `AIToolCapabilityCatalog` immediately before `AIToolsRegistry`.
3. Register `MediaAIToolActionProvider` and `IAIToolActionProvider` after `AIMediaToolService`.
4. Register `CleanerAIToolActionProvider` and `IAIToolActionProvider` after `CleanerRuleService`.
5. Do not include theme, font, window or unrelated service changes.

Safe staging sequence, part 1 — create an exact candidate from `HEAD`:

```powershell
$backup = 'App.xaml.cs.ai-current'
if (Test-Path $backup) { throw 'AI App 临时备份已存在' }
Copy-Item -LiteralPath 'App.xaml.cs' -Destination $backup
$headApp = (& git show HEAD:App.xaml.cs) -join "`n"
$oldTools = @(
 '            services.AddTransient<HomeTool>();',
 '            services.AddTransient<MediaManagerTool>();',
 '            services.AddTransient<CleanerAssistantTool>();',
 '            services.AddTransient<BlueSapphire.Tools.AICopilotTool>();',
 '            services.AddTransient<BlueSapphire.Tools.AITaskCenterTool>();'
) -join "`n"
$newTools = @(
 '            services.AddTransient<HomeTool>();',
 '            services.AddSingleton<MediaManagerTool>();',
 '            services.AddSingleton<CleanerAssistantTool>();',
 '            services.AddSingleton<BlueSapphire.Interfaces.IAIToolCapabilityProvider>(sp => sp.GetRequiredService<MediaManagerTool>());',
 '            services.AddSingleton<BlueSapphire.Interfaces.IAIToolCapabilityProvider>(sp => sp.GetRequiredService<CleanerAssistantTool>());'
) -join "`n"
$headApp = $headApp.Replace($oldTools, $newTools)
$headApp = $headApp.Replace(
 '            services.AddSingleton<BlueSapphire.Services.DeepSeekAIService>();' + "`n" + '            services.AddSingleton<BlueSapphire.Services.AIToolsRegistry>();',
 '            services.AddSingleton<BlueSapphire.Services.DeepSeekAIService>();' + "`n" + '            services.AddSingleton<BlueSapphire.Services.AIToolCapabilityCatalog>();' + "`n" + '            services.AddSingleton<BlueSapphire.Services.AIToolsRegistry>();')
$headApp = $headApp.Replace(
 '            services.AddSingleton<BlueSapphire.Services.AIMediaToolService>();' + "`n" + '            services.AddSingleton<BlueSapphire.Services.AIDiagnosticsService>();',
 '            services.AddSingleton<BlueSapphire.Services.AIMediaToolService>();' + "`n" + '            services.AddSingleton<BlueSapphire.Services.MediaAIToolActionProvider>();' + "`n" + '            services.AddSingleton<BlueSapphire.Interfaces.IAIToolActionProvider>(sp =>' + "`n" + '                sp.GetRequiredService<BlueSapphire.Services.MediaAIToolActionProvider>());' + "`n" + '            services.AddSingleton<BlueSapphire.Services.AIDiagnosticsService>();')
$headApp = $headApp.Replace(
 '            services.AddSingleton<BlueSapphire.Services.CleanerRuleService>();' + "`n" + '            services.AddSingleton<BlueSapphire.Services.CleanerStateStore>();',
 '            services.AddSingleton<BlueSapphire.Services.CleanerRuleService>();' + "`n" + '            services.AddSingleton<BlueSapphire.Services.CleanerAIToolActionProvider>();' + "`n" + '            services.AddSingleton<BlueSapphire.Interfaces.IAIToolActionProvider>(sp =>' + "`n" + '                sp.GetRequiredService<BlueSapphire.Services.CleanerAIToolActionProvider>());' + "`n" + '            services.AddSingleton<BlueSapphire.Services.CleanerStateStore>();')
if ($headApp.Contains('AICopilotTool') -or $headApp.Contains('AITaskCenterTool')) {
    throw '旧 AI 工具注册仍存在于候选 App'
}
Set-Content -LiteralPath 'App.xaml.cs' -Value $headApp -Encoding utf8
```

Part 2 — stage the candidate:

```powershell
git add -- App.xaml.cs
git diff --cached -- App.xaml.cs
```

Part 3 — restore the user worktree content and move the temporary backup out of the repository:

```powershell
Copy-Item -LiteralPath 'App.xaml.cs.ai-current' -Destination 'App.xaml.cs' -Force
$backupTarget = Join-Path $env:TEMP ('App.xaml.cs.ai-current.' + [Guid]::NewGuid().ToString('N'))
Move-Item -LiteralPath 'App.xaml.cs.ai-current' -Destination $backupTarget
```
- [ ] **Step 3: Stage task-center files**

```powershell
git add -- Services/AITaskCenterService.cs BlueSapphire.Tests/AITaskCenterServiceTests.cs Models/AppMessages.cs
git diff --cached --check
```

- [ ] **Step 4: Isolate and commit**

```powershell
.\scripts\verify-ai-slice.ps1 -Name ai-task-di -TestFilter "FullyQualifiedName~AITaskCenterServiceTests|FullyQualifiedName~AITool|FullyQualifiedName~CleanerAIToolActionProviderTests|FullyQualifiedName~MediaAIToolActionProviderTests"
git commit -m "feat: 统一 AI Provider 注册与任务中心状态"
```

- [ ] **Step 5: Verify unrelated App changes remain**

```powershell
git status --short -- App.xaml.cs
git diff -- App.xaml.cs
```

Expected: global theme/window/UI changes remain uncommitted.

---

### Task 5: Merge the AI workspace and delete legacy entries

**Files:** Slice D files.

**Interfaces:** Produces one workspace for conversation, tasks and recent activity with no legacy navigation targets.

- [ ] **Step 1: Stage AI workspace files and deletions**

```powershell
git add -- HomePage.xaml HomePage.xaml.cs Views/AICopilotPage.xaml Views/AICopilotPage.xaml.cs Tools/HomeTool.cs
git add -u -- Tools/AICopilotTool.cs Tools/AITaskCenterTool.cs Views/AITaskCenterPage.xaml Views/AITaskCenterPage.xaml.cs
git diff --cached --check
```

- [ ] **Step 2: Verify legacy references are gone in the candidate**

```powershell
git grep --cached -n "AITaskCenterPage\|AITaskCenterTool\|AICopilotTool"
if ($LASTEXITCODE -eq 0) { throw '候选提交仍引用旧 AI 入口' }
```

- [ ] **Step 3: Check XAML resource closure**

Before adding any theme file, isolate the candidate. If XAML compilation reports missing resource keys, compare current AI page references with `HEAD:Themes/SharedTheme.xaml` and add only the missing aliases/styles to a minimal staged theme candidate. Do not stage the full current theme diff.

- [ ] **Step 4: Isolate and commit**

```powershell
.\scripts\verify-ai-slice.ps1 -Name ai-workspace -TestFilter "FullyQualifiedName~AI"
git commit -m "feat: 合并 AI 工作台与任务中心入口"
```

If Home visual changes prove inseparable from the AI host through XAML named-element or code-behind dependencies, include only Home and AI page files in this closure and document the compiler evidence. Do not add Settings, Media page, MainWindow or global theme redesign.

---

### Task 6: Review architecture and add missing regression tests

**Files:** AI files committed in Tasks 2–5.

- [ ] **Step 1: Review action/capability parity**

Build sets of model-visible action names and registered handler names. Every destructive or confirmation-required capability must have exactly one domain handler; unknown or missing handlers are defects.

- [ ] **Step 2: Review observer and cancellation failure paths**

Verify:

- provider exceptions return structured failures;
- confirmation denial performs no write;
- cancellation remains cancellation, not completion;
- task progress cannot decrease;
- provider registration cannot mutate capability snapshots;
- no local full path appears in Cleaner summaries.

- [ ] **Step 3: Add RED tests for each discovered defect**

For every concrete defect, run the failing focused test before applying a root-cause fix. Run the nearest AI suite and an isolated slice build before committing.

- [ ] **Step 4: Commit review fixes separately**

```powershell
git commit -m "fix: 加固 AI 工具分发与任务状态"
```

Skip this commit only if no production defect is found.

---

### Task 7: Update architecture evidence and perform final verification

**Files:**
- Modify: `docs/ai-assistant-architecture.md`
- Create: `docs/ai-tool-workflow-acceptance.md`
- Modify: `docs/superpowers/plans/2026-07-22-ai-tool-architecture-consolidation.md`

- [ ] **Step 1: Run AI-focused and full tests with history guard**

```powershell
$hashBefore = (Get-FileHash -Algorithm SHA256 Assets\DevMatrixLog.json).Hash
dotnet test BlueSapphire.Tests\BlueSapphire.Tests.csproj --no-restore -v:minimal --filter "FullyQualifiedName~AI"
dotnet test BlueSapphire.Tests\BlueSapphire.Tests.csproj --no-restore -v:minimal
$hashAfter = (Get-FileHash -Algorithm SHA256 Assets\DevMatrixLog.json).Hash
if ($hashBefore -ne $hashAfter) { throw '测试修改了 DevMatrixLog.json' }
```

- [ ] **Step 2: Verify clean committed HEAD independently**

Create `$finalPath = 'C:\bsw\final_' + [Guid]::NewGuid().ToString('N').Substring(0, 8)`, add a detached worktree at `HEAD`, run restore, full test and no-incremental build, then remove it normally.

- [ ] **Step 3: Run main worktree build**

```powershell
dotnet build BlueSapphire.slnx --no-incremental --no-restore -v:minimal
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 4: Launch and close the real AI workspace**

```powershell
$exe = Resolve-Path 'bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\BlueSapphire.exe'
$p = Start-Process -FilePath $exe -ArgumentList '--tool=Home' -PassThru
```

Verify nonzero handle and `Responding=True`, inspect that conversation/tasks/recent activity host loads, then use `CloseMainWindow()` and require exit code 0. Leave a final verified instance running after all documentation commits.

- [ ] **Step 5: Update documents with exact counts and limitations**

Document AI-focused count, complete-worktree count, isolated-HEAD count, build result, real window result, deleted legacy entries, and untested remote-provider/destructive real-world paths.

- [ ] **Step 6: Final boundary audit**

```powershell
git status --short --branch
git diff --cached --name-only
git grep -n "AITaskCenterPage\|AITaskCenterTool\|AICopilotTool" HEAD
git ls-files Interfaces/IAIToolActionProvider.cs Interfaces/IAIToolCapabilityProvider.cs Models/AIToolCapabilityModels.cs Services/AIToolActionHandlerRegistry.cs Services/AIToolCapabilityCatalog.cs Services/CleanerAIToolActionProvider.cs Services/MediaAIToolActionProvider.cs
```

Expected:

- all AI architecture files tracked;
- no staged files;
- no legacy references in HEAD;
- global theme and unrelated UI changes remain outside AI commits;
- `DevMatrixLog.json` remains clean and unchanged.
