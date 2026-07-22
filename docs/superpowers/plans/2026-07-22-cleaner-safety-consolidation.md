# Cleaner Safety Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将当前 Cleaner 安全重构整理为依赖闭合、可独立构建、可回滚的提交，并重新验证扫描、清理、恢复、取消、共享互斥和空间统计。

**Architecture:** 按“扫描规则”“执行恢复”“协调状态”“UI 证据”四个责任切片暂存。每个候选切片从当前 `HEAD` 创建临时 Git 工作树，只应用 staged patch，再执行 restore、聚焦测试和无增量构建。`App.xaml.cs` 与 `Themes/SharedTheme.xaml` 含有其他重构，使用从 `HEAD` 生成的最小 staged blob，只提交 Cleaner 必需内容，主工作树中的其他修改继续保留。

**Tech Stack:** C#、.NET 8、WinUI 3、xUnit、PowerShell 7、Git worktree、Microsoft.Extensions.DependencyInjection。

## Global Constraints

- `Assets/DevMatrixLog.json` 全阶段保持 SHA-256 `A9460CC2C03CFED65B36BE74E91D5CD2FBACD978D4243F8E6EFB9C81EBB64FEC`。
- 不执行 `git add .`、`git reset --hard`、`git clean` 或覆盖当前其他重构。
- 不拆分 `CleanerScanService.cs` 和 `CleanerExecutionService.cs`。
- 不新增扫描规则，不扩大默认选择、自动清理或永久删除范围。
- 自动保洁只处理默认选中的低风险、可执行且可恢复对象。
- AI 不得改变 Cleaner 风险、边界、驱动器范围、执行方式或确认要求。
- 每个候选提交必须在临时隔离工作树中独立 restore、测试和构建。
- Cleaner 聚焦测试不少于当前 101 项；全量测试不少于当前 216 项；均为零失败。
- `dotnet build BlueSapphire.slnx --no-incremental --no-restore -v:minimal` 必须为 0 警告、0 错误。
- 真实 Cleaner 窗口必须获得非零句柄、`Responding=True`，正常关闭后退出码为 0。

---

## File Map

### Verification tooling

- Create: `scripts/verify-cleaner-slice.ps1` — 验证 staged patch 在临时工作树中能够独立测试和构建。

### Slice A — rules, models, paths and scan scope

- `Assets/CleanerRules.json`
- `Models/CleanerModels.cs`
- `Services/CleanerApplicationDiscoveryService.cs`
- `Services/CleanerKnownPathResolver.cs`
- `Services/CleanerDriveService.cs`
- `Services/CleanerPathSafety.cs`
- `Services/CleanerRiskEvaluator.cs`
- `Services/CleanerRuleService.cs`
- `Services/CleanerScanService.cs`
- `Services/CleanerDeepScanService.cs`
- `Services/CleanerOrphanResidueService.cs`
- `Services/CleanerSpaceAnalysisService.cs`
- `Services/CleanerStateStore.cs`
- 对应 Cleaner 测试。

### Slice B — execution, restore and accounting

- `Services/CleanerAuditService.cs`
- `Services/CleanerExecutionService.cs`
- `Services/CleanerSystemCleanupService.cs`
- `BlueSapphire.Tests/CleanerExecutionServiceTests.cs`
- `BlueSapphire.Tests/CleanerSystemCleanupServiceTests.cs`
- `BlueSapphire.Tests/CleanerP0AcceptanceTests.cs`

### Slice C — coordinator and ViewModel workflow

- `Services/CleanerOperationCoordinator.cs`
- `ViewModels/Cleaner/*.cs`
- `ViewModels/CleanerAssistantViewModel.cs`
- `ViewModels/CleanerAssistantViewModel.Properties.cs`
- `ViewModels/CleanerSettingsViewModel.cs`
- `Interfaces/ICleanerAssistantViewInteraction.cs`
- Cleaner coordinator/ViewModel tests。
- `App.xaml.cs` 仅暂存 Cleaner DI 增量。

### Slice D — Cleaner UI and evidence

- `CleanerAssistantPage.xaml`
- `CleanerAssistantPage.xaml.cs`
- `Themes/SharedTheme.xaml` 仅暂存 Cleaner XAML 缺少的三个兼容资源。
- `docs/cleaner-functional-audit.md`
- `docs/cleaner-workflow-acceptance.md`
- `docs/superpowers/plans/2026-07-19-cleaner-p0-safety.md`

### Deferred to AI consolidation

- `Services/CleanerAIToolActionProvider.cs`
- `BlueSapphire.Tests/CleanerAIToolActionProviderTests.cs`
- 通用 AI action/capability 接口、目录和注册表。

除非隔离构建证明 Cleaner 核心直接依赖这些文件，否则本阶段不提交它们。

---

### Task 1: Add repeatable isolated-slice verification

**Files:**
- Create: `scripts/verify-cleaner-slice.ps1`

**Interfaces:**
- Consumes: 当前 Git 暂存区中的完整候选切片。
- Produces: 只有补丁可应用、restore 成功、指定测试通过且无增量构建成功时才返回 0。

- [ ] **Step 1: Create the verifier**

Create `scripts/verify-cleaner-slice.ps1`:

```powershell
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9._-]+$')]
    [string]$Name,
    [string]$TestFilter = 'FullyQualifiedName~Cleaner'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (& git rev-parse --show-toplevel).Trim()
if (-not $repoRoot) { throw '无法解析 Git 仓库根目录。' }

& git diff --cached --quiet
if ($LASTEXITCODE -eq 0) { throw 'Git 暂存区为空，无法验证候选切片。' }

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) 'BlueSapphireCleanerSlices'
$runId = $Name + '_' + [Guid]::NewGuid().ToString('N')
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
    }
    & git worktree prune
    if (Test-Path -LiteralPath $patchPath) {
        Remove-Item -LiteralPath $patchPath -Force
    }
}
```

- [ ] **Step 2: Prove empty-index rejection**

```powershell
.\scripts\verify-cleaner-slice.ps1 -Name empty-index-check
```

Expected: FAIL with `Git 暂存区为空，无法验证候选切片。`

- [ ] **Step 3: Syntax-check and commit**

```powershell
$tokens = $null
$errors = $null
[Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path '.\scripts\verify-cleaner-slice.ps1'),
    [ref]$tokens,
    [ref]$errors) | Out-Null
if ($errors.Count -gt 0) { $errors; throw 'PowerShell 语法检查失败' }

git add -- scripts/verify-cleaner-slice.ps1
git diff --cached --name-only
git diff --cached --check
git commit -m "test: 添加 Cleaner 切片隔离验证"
```

Expected staged file exactly `scripts/verify-cleaner-slice.ps1`.

---

### Task 2: Consolidate scan scope, rules and model contracts

**Files:** Slice A files from File Map.

**Interfaces:** Produces selected-drive snapshot behavior, stable risk baseline, application context, state migration and accounting fields used by later slices.

- [ ] **Step 1: Run focused baseline tests**

```powershell
$devLogHash = (Get-FileHash -Algorithm SHA256 Assets\DevMatrixLog.json).Hash
if ($devLogHash -ne 'A9460CC2C03CFED65B36BE74E91D5CD2FBACD978D4243F8E6EFB9C81EBB64FEC') {
    throw "DevMatrixLog 基线异常: $devLogHash"
}

dotnet test BlueSapphire.Tests\BlueSapphire.Tests.csproj --no-restore -v:minimal --filter "FullyQualifiedName~CleanerScanServiceTests|FullyQualifiedName~CleanerRiskEvaluatorTests|FullyQualifiedName~CleanerStateStoreTests|FullyQualifiedName~CleanerDriveOptionTests|FullyQualifiedName~CleanerApplicationDiscoveryServiceTests|FullyQualifiedName~CleanerRuleServiceTests|FullyQualifiedName~CleanerOrphanResidueServiceTests|FullyQualifiedName~CleanerModelBehaviorTests"
```

Expected: all selected tests pass.

- [ ] **Step 2: Stage exact Slice A candidate**

```powershell
$sliceA = @(
 'Assets/CleanerRules.json',
 'Models/CleanerModels.cs',
 'Services/CleanerApplicationDiscoveryService.cs',
 'Services/CleanerKnownPathResolver.cs',
 'Services/CleanerDriveService.cs',
 'Services/CleanerPathSafety.cs',
 'Services/CleanerRiskEvaluator.cs',
 'Services/CleanerRuleService.cs',
 'Services/CleanerScanService.cs',
 'Services/CleanerDeepScanService.cs',
 'Services/CleanerOrphanResidueService.cs',
 'Services/CleanerSpaceAnalysisService.cs',
 'Services/CleanerStateStore.cs',
 'BlueSapphire.Tests/CleanerApplicationDiscoveryServiceTests.cs',
 'BlueSapphire.Tests/CleanerDriveOptionTests.cs',
 'BlueSapphire.Tests/CleanerModelBehaviorTests.cs',
 'BlueSapphire.Tests/CleanerOrphanResidueServiceTests.cs',
 'BlueSapphire.Tests/CleanerRiskEvaluatorTests.cs',
 'BlueSapphire.Tests/CleanerScanServiceTests.cs',
 'BlueSapphire.Tests/CleanerStateStoreTests.cs'
)
git add -- $sliceA
git diff --cached --name-only
git diff --cached --check
```

- [ ] **Step 3: Verify dependency closure**

```powershell
.\scripts\verify-cleaner-slice.ps1 -Name scan-rules -TestFilter "FullyQualifiedName~CleanerScanServiceTests|FullyQualifiedName~CleanerRiskEvaluatorTests|FullyQualifiedName~CleanerStateStoreTests|FullyQualifiedName~CleanerDriveOptionTests|FullyQualifiedName~CleanerApplicationDiscoveryServiceTests|FullyQualifiedName~CleanerRuleServiceTests|FullyQualifiedName~CleanerOrphanResidueServiceTests|FullyQualifiedName~CleanerModelBehaviorTests"
```

Expected: restore, tests and build pass.

If a compiler error names a missing Cleaner type, locate its declaration with:

```powershell
rg -n "(class|record|enum)" Models Services ViewModels
```

Add only that declaring file and its direct tests. If it belongs to Slice B or C and changes behavior, merge only the adjacent dependent slice and record the exact compiler error in the execution result. Do not add AI, media or theme files.

- [ ] **Step 4: Commit and re-test**

```powershell
git diff --cached --check
git commit -m "fix: 收口 Cleaner 扫描范围与风险规则"
```

Run the Step 1 filter again. Expected: all pass and `DevMatrixLog.json` hash remains unchanged.

---

### Task 3: Consolidate execution, restore, purge and accounting

**Files:** Slice B files from File Map.

**Interfaces:** Consumes Slice A models/state/path safety; produces permanent, quarantine and system execution paths with separated released/recoverable accounting.

- [ ] **Step 1: Verify required regression scenarios exist**

```powershell
$required = @(
 'ExecuteAsync_UsesPerTargetSizesAndSeparatesRecoverableBytes',
 'ExecuteAsync_CountsPermanentDeletionAsReleasedSpace',
 'RestoreLatestAsync_CancellationPersistsEntriesRestoredBeforeCancellation',
 'PurgeQuarantineAsync_CancellationPersistsEntriesPurgedBeforeCancellation',
 'RetryFailedEntriesAsync_CancellationPersistsEntriesRetriedBeforeCancellation',
 'ControlledFilesystemFlow_ScanQuarantineRestoreAndPurgeMatchesAccounting',
 'DeliveryOptimizationCommand_IsFixedNonInteractiveAndHidden'
)
$text = Get-Content BlueSapphire.Tests\CleanerExecutionServiceTests.cs,BlueSapphire.Tests\CleanerP0AcceptanceTests.cs,BlueSapphire.Tests\CleanerSystemCleanupServiceTests.cs -Raw
foreach ($name in $required) {
    if (-not $text.Contains($name)) { throw "缺少执行安全测试: $name" }
}
```

- [ ] **Step 2: Run execution tests**

```powershell
dotnet test BlueSapphire.Tests\BlueSapphire.Tests.csproj --no-restore -v:minimal --filter "FullyQualifiedName~CleanerExecutionServiceTests|FullyQualifiedName~CleanerSystemCleanupServiceTests|FullyQualifiedName~CleanerP0AcceptanceTests"
```

Expected: all selected tests pass. Existing user fixes are regression capture; do not revert them merely to manufacture RED.

- [ ] **Step 3: Stage, isolate and commit**

```powershell
$sliceB = @(
 'Services/CleanerAuditService.cs',
 'Services/CleanerExecutionService.cs',
 'Services/CleanerSystemCleanupService.cs',
 'BlueSapphire.Tests/CleanerExecutionServiceTests.cs',
 'BlueSapphire.Tests/CleanerSystemCleanupServiceTests.cs',
 'BlueSapphire.Tests/CleanerP0AcceptanceTests.cs'
)
git add -- $sliceB
git diff --cached --check
.\scripts\verify-cleaner-slice.ps1 -Name execution-recovery -TestFilter "FullyQualifiedName~CleanerExecutionServiceTests|FullyQualifiedName~CleanerSystemCleanupServiceTests|FullyQualifiedName~CleanerP0AcceptanceTests"
git commit -m "fix: 收口 Cleaner 执行恢复与空间统计"
```

---

### Task 4: Consolidate the shared operation gate and workflow state

**Files:** Slice C files from File Map.

**Interfaces:** Consumes Slice A/B services; produces one shared operation owner and accurate command availability.

- [ ] **Step 1: Run coordinator and ViewModel tests**

```powershell
dotnet test BlueSapphire.Tests\BlueSapphire.Tests.csproj --no-restore -v:minimal --filter "FullyQualifiedName~CleanerOperationCoordinatorTests|FullyQualifiedName~CleanerAssistantViewModelTests|FullyQualifiedName~CleanerCleanupViewModelTests"
```

Expected: all pass, including automatic cleanup leaving low-risk permanent items for manual review.

- [ ] **Step 2: Stage a Cleaner-only `App.xaml.cs` blob**

```powershell
$tempApp = Join-Path $env:TEMP ('BlueSapphire_App_Cleaner_' + [Guid]::NewGuid().ToString('N') + '.cs')
$headApp = (& git show HEAD:App.xaml.cs) -join "`n"
$old = @(
 '            services.AddSingleton<BlueSapphire.Services.CleanerExecutionService>();',
 '            services.AddSingleton<BlueSapphire.Services.AIMemoryService>();'
) -join "`n"
$new = @(
 '            services.AddSingleton<BlueSapphire.Services.CleanerExecutionService>();',
 '            services.AddSingleton<BlueSapphire.Services.CleanerOperationCoordinator>();',
 '            services.AddSingleton<BlueSapphire.Services.CleanerSystemCleanupService>();',
 '            services.AddSingleton<BlueSapphire.Services.CleanerApplicationDiscoveryService>();',
 '            services.AddSingleton<BlueSapphire.Services.AIMemoryService>();'
) -join "`n"
if (-not $headApp.Contains($old)) { throw '无法定位 Cleaner DI 插入点。' }
Set-Content -LiteralPath $tempApp -Value $headApp.Replace($old, $new) -Encoding utf8 -NoNewline
$appBlob = (& git hash-object -w $tempApp).Trim()
& git update-index --add --cacheinfo 100644 $appBlob App.xaml.cs
Remove-Item -LiteralPath $tempApp -Force
```

- [ ] **Step 3: Stage Slice C and isolate**

```powershell
$sliceC = @(
 'Services/CleanerOperationCoordinator.cs',
 'ViewModels/Cleaner/CleanerAutomationViewModel.cs',
 'ViewModels/Cleaner/CleanerCleanupViewModel.cs',
 'ViewModels/Cleaner/CleanerDriveSelectionViewModel.cs',
 'ViewModels/Cleaner/CleanerRuleManagementViewModel.cs',
 'ViewModels/Cleaner/CleanerScanViewModel.cs',
 'ViewModels/CleanerAssistantViewModel.cs',
 'ViewModels/CleanerAssistantViewModel.Properties.cs',
 'ViewModels/CleanerSettingsViewModel.cs',
 'Interfaces/ICleanerAssistantViewInteraction.cs',
 'BlueSapphire.Tests/CleanerAssistantViewModelTests.cs',
 'BlueSapphire.Tests/CleanerCleanupViewModelTests.cs',
 'BlueSapphire.Tests/CleanerOperationCoordinatorTests.cs'
)
git add -- $sliceC
git diff --cached --check
.\scripts\verify-cleaner-slice.ps1 -Name coordinator-workflow -TestFilter "FullyQualifiedName~CleanerOperationCoordinatorTests|FullyQualifiedName~CleanerAssistantViewModelTests|FullyQualifiedName~CleanerCleanupViewModelTests"
```

Expected: staged files are `App.xaml.cs` plus Slice C; isolated tests/build pass.

- [ ] **Step 4: Commit and preserve unrelated App changes**

```powershell
git commit -m "fix: 统一 Cleaner 操作协调和工作流状态"
git status --short -- App.xaml.cs
git diff -- App.xaml.cs
```

Expected: current broad AI/theme App modifications remain visible and uncommitted.

---

### Task 5: Consolidate Cleaner UI safety bindings with minimal theme compatibility

**Files:** Slice D UI files.

**Interfaces:** Consumes Slice C command availability and view interaction contract; produces compiled controls whose enabled state matches the operation gate.

- [ ] **Step 1: Stage Cleaner page files**

```powershell
git add -- CleanerAssistantPage.xaml CleanerAssistantPage.xaml.cs
```

- [ ] **Step 2: Stage a minimal theme blob from HEAD**

Current Cleaner XAML requires three resources absent from `HEAD`: `AccentPrimary`, `AccentPrimaryBg`, `TextStyle_MetricLarge`.

```powershell
$tempTheme = Join-Path $env:TEMP ('BlueSapphire_Theme_Cleaner_' + [Guid]::NewGuid().ToString('N') + '.xaml')
$headTheme = (& git show HEAD:Themes/SharedTheme.xaml) -join "`n"
$anchor = '    <SolidColorBrush x:Key="AccentCyanBg" Color="#2426AFC7"/>'
$aliases = @(
 '    <StaticResource x:Key="AccentPrimary" ResourceKey="AccentCyan"/>',
 '    <StaticResource x:Key="AccentPrimaryBg" ResourceKey="AccentCyanBg"/>'
) -join "`n"
if (-not $headTheme.Contains($anchor)) { throw '未找到主题画刷插入点。' }
$headTheme = $headTheme.Replace($anchor, $anchor + "`n" + $aliases)
$style = @(
 '    <Style x:Key="TextStyle_MetricLarge" TargetType="TextBlock">',
 '        <Setter Property="FontSize" Value="24"/>',
 '        <Setter Property="FontWeight" Value="Bold"/>',
 '        <Setter Property="FontFamily" Value="Segoe UI Variable Display, Segoe UI"/>',
 '        <Setter Property="Foreground" Value="{ThemeResource TextMain}"/>',
 '    </Style>'
) -join "`n"
$headTheme = $headTheme.Replace('</ResourceDictionary>', $style + "`n</ResourceDictionary>")
Set-Content -LiteralPath $tempTheme -Value $headTheme -Encoding utf8 -NoNewline
$themeBlob = (& git hash-object -w $tempTheme).Trim()
& git update-index --add --cacheinfo 100644 $themeBlob Themes/SharedTheme.xaml
Remove-Item -LiteralPath $tempTheme -Force
```

- [ ] **Step 3: Isolate and commit UI**

```powershell
git diff --cached --check
.\scripts\verify-cleaner-slice.ps1 -Name cleaner-ui -TestFilter "FullyQualifiedName~CleanerAssistantViewModelTests|FullyQualifiedName~CleanerCleanupViewModelTests"
git commit -m "fix: 完成 Cleaner 安全界面接线"
```

- [ ] **Step 4: Preserve the broad theme redesign**

```powershell
git status --short -- Themes/SharedTheme.xaml
git diff -- Themes/SharedTheme.xaml
```

Expected: unrelated global theme changes remain uncommitted.

---

### Task 6: Verify and defer the Cleaner AI provider dependency

**Files:** Inspect only unless isolated build proves a direct dependency.

- [ ] **Step 1: Search direct references**

```powershell
rg -n "CleanerAIToolActionProvider|IAIToolActionProvider|AIToolActionHandlerRegistry" App.xaml.cs CleanerAssistantPage.xaml.cs Models Services ViewModels --glob '*.cs'
```

- [ ] **Step 2: Run provider tests in the complete worktree**

```powershell
dotnet test BlueSapphire.Tests\BlueSapphire.Tests.csproj --no-restore -v:minimal --filter "FullyQualifiedName~CleanerAIToolActionProviderTests"
```

Expected: provider tests pass, while core Cleaner commits remain independent of the uncommitted provider.

- [ ] **Step 3: Record the deferral**

Add this factual statement to `docs/cleaner-workflow-acceptance.md`:

```text
Cleaner AI 动作 Provider 已在完整工作树通过专项测试，但其通用 action 接口、处理器注册表和 App 注册属于 AI 架构依赖闭包，延后到 AI 收口阶段提交。Cleaner 核心扫描、风险、执行和 UI 不依赖该未提交 Provider。
```

If isolated build proves a direct dependency, create a separate dependency-closed commit containing only the provider and exact required common interfaces. Do not include Media provider or AI page changes.

---

### Task 7: Update evidence and run final verification

**Files:** Cleaner facts, acceptance and P0 execution plan documents.

- [ ] **Step 1: Run Cleaner-focused tests**

```powershell
$sourceHashBefore = (Get-FileHash -Algorithm SHA256 Assets\DevMatrixLog.json).Hash
dotnet test BlueSapphire.Tests\BlueSapphire.Tests.csproj --no-restore -v:minimal --filter "FullyQualifiedName~Cleaner"
```

Expected: at least 101 passed, 0 failed.

- [ ] **Step 2: Run full tests and verify history hash**

```powershell
dotnet test BlueSapphire.Tests\BlueSapphire.Tests.csproj --no-restore -v:minimal
$sourceHashAfter = (Get-FileHash -Algorithm SHA256 Assets\DevMatrixLog.json).Hash
if ($sourceHashBefore -ne $sourceHashAfter) { throw '测试修改了 DevMatrixLog.json' }
```

Expected: at least 216 passed, 0 failed, hash unchanged.

- [ ] **Step 3: Run no-incremental build**

```powershell
dotnet build BlueSapphire.slnx --no-incremental --no-restore -v:minimal
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 4: Launch and normally close Cleaner**

```powershell
$exe = Resolve-Path 'bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\BlueSapphire.exe'
$p = Start-Process -FilePath $exe -ArgumentList '--tool=CleanerAssistant' -PassThru
try {
 $deadline = (Get-Date).AddSeconds(30)
 $ready = $false
 do {
  Start-Sleep -Milliseconds 500
  $p.Refresh()
  if ($p.HasExited) { break }
  if ($p.MainWindowHandle -ne 0 -and $p.Responding) { $ready = $true; break }
 } while ((Get-Date) -lt $deadline)
 if (-not $ready) { throw 'Cleaner 窗口未进入可响应状态' }
 Write-Host "Handle=$($p.MainWindowHandle) Responding=$($p.Responding)"
 if (-not $p.CloseMainWindow()) { throw '无法发送正常关闭请求' }
 if (-not $p.WaitForExit(15000)) { throw '正常关闭后未退出' }
 if ($p.ExitCode -ne 0) { throw "退出码异常: $($p.ExitCode)" }
}
finally {
 if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
}
```

- [ ] **Step 5: Update documents with exact evidence**

Replace stale `201/201` and unchecked P0 steps with actual current counts and commands. Do not mark real system-directory deletion or unavailable cross-volume hardware paths as executed.

- [ ] **Step 6: Commit evidence documents**

```powershell
git add -- docs/cleaner-functional-audit.md docs/cleaner-workflow-acceptance.md docs/superpowers/plans/2026-07-19-cleaner-p0-safety.md
git diff --cached --check
git commit -m "docs: 更新 Cleaner 真实验收记录"
```

- [ ] **Step 7: Final boundary audit**

```powershell
git log --oneline --decorate origin/master..HEAD
git status --short --branch
git diff --check -- Assets/DevMatrixLog.json
git ls-files Services/CleanerOperationCoordinator.cs Services/CleanerSystemCleanupService.cs Services/CleanerApplicationDiscoveryService.cs BlueSapphire.Tests/CleanerOperationCoordinatorTests.cs BlueSapphire.Tests/CleanerSystemCleanupServiceTests.cs BlueSapphire.Tests/CleanerApplicationDiscoveryServiceTests.cs
```

Expected:

- required Cleaner core files are tracked;
- `Assets/DevMatrixLog.json` is clean and unchanged;
- Cleaner AI provider is explicitly deferred or has its own dependency-closed commit;
- Media and broad theme/UI changes remain outside Cleaner commits;
- no staged files remain.
