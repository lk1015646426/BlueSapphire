# Cleaner P0 Safety Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让自动保洁、所有磁盘入口、取消后的历史记录和 Debug 测试数据满足清理器的安全不变量。

**Architecture:** 新增一个会话级命名信号量协调器，由 UI、自动化和 AI 编排层显式持有租约；执行服务在取消边界持久化部分结果；开发日志彻底移除运行数据反写源码的路径。各项先以最小失败测试证明缺陷，再实施单一根因修复。

**Tech Stack:** C# 12/.NET 8、WinUI 3、CommunityToolkit.Mvvm、xUnit、Windows 会话级命名 `Semaphore`。

## Global Constraints

- `Assets/DevMatrixLog.json` 只追加，禁止删除、覆盖、改写或重排现有节点。
- 保留工作区中与本任务无关的全部用户改动。
- 全量测试不少于 131 项且零失败。
- `dotnet build BlueSapphire.slnx --no-incremental --no-restore -v:minimal` 必须零警告、零错误。
- 不在 UI 线程执行目录枚举、删除、跨盘复制或进程检查。

---

### Task 1: 自动保洁只执行可恢复动作

**Files:**
- Modify: `BlueSapphire.Tests/CleanerAssistantViewModelTests.cs`
- Modify: `ViewModels/CleanerAssistantViewModel.cs`
- Modify: `CleanerAssistantPage.xaml`

**Interfaces:**
- Consumes: `CleanerScanItem.ExecutionMode`、`CleanerRiskLevel.Low`。
- Produces: 自动保洁候选过滤规则 `ExecutionMode is Quarantine or RecycleBin`。

- [ ] 新增真实临时文件测试：同一次扫描同时命中 `Quarantine` 与 `Permanent` 低风险规则，执行后只移走可恢复文件。
- [ ] 运行该测试，确认当前实现错误删除永久项。
- [ ] 在 `ExecuteAutomaticLowRiskCleanupAsync` 中加入可恢复执行方式过滤，并调整设置文案明确“自动移入可恢复暂存”。
- [ ] 运行自动化相关测试并确认通过。
- [ ] 仅在用户要求提交时暂存并提交本任务文件，避免把脏工作区的其他改动带入提交。

### Task 2: 统一进程内和跨进程操作协调器

**Files:**
- Create: `Services/CleanerOperationCoordinator.cs`
- Create: `BlueSapphire.Tests/CleanerOperationCoordinatorTests.cs`
- Modify: `App.xaml.cs`
- Modify: `ViewModels/Cleaner/CleanerScanViewModel.cs`
- Modify: `ViewModels/Cleaner/CleanerCleanupViewModel.cs`
- Modify: `ViewModels/CleanerAssistantViewModel.cs`
- Modify: `Services/CleanerAIToolActionProvider.cs`
- Modify: affected constructor tests

**Interfaces:**
- Produces: `TryAcquire(CleanerOperationKind kind, out CleanerOperationLease? lease)`、`IsBusy`、`StateChanged`。
- Consumes: UI/AI/自动化编排在调用扫描或执行服务前申请的租约。

- [ ] 新增两个协调器实例使用相同名字时互斥、释放后可再次获取、重复释放安全的测试。
- [ ] 运行测试，确认类型缺失而失败。
- [ ] 实现命名 `Semaphore` 协调器和一次性租约并注册为单例。
- [ ] 把扫描、手动清理、自动保洁、重试、恢复、清空和 AI 扫描/清理接入协调器；自动保洁通过显式既有租约覆盖完整事务。
- [ ] 运行协调器、ViewModel 与 AI 提供器测试并确认通过。
- [ ] 仅在用户要求提交时提交本任务文件。

### Task 3: 取消后保存部分恢复、清空和重试结果

**Files:**
- Modify: `BlueSapphire.Tests/CleanerExecutionServiceTests.cs`
- Modify: `Services/CleanerExecutionService.cs`

**Interfaces:**
- Produces: 带 `IProgress<CleanerExecutionProgress>?` 的恢复/清空重载；取消捕获路径写入历史。
- Consumes: `CleanerStateStore.SaveHistoryAsync`。

- [ ] 为恢复、清空隔离区、失败重试分别新增“首项完成、第二项开始时取消”的真实文件系统测试。
- [ ] 运行三个测试，确认历史仍保留旧状态而失败。
- [ ] 在每个循环的取消捕获路径更新批次统计并保存历史，然后重新抛出取消异常。
- [ ] 运行 `CleanerExecutionServiceTests` 并确认通过。
- [ ] 仅在用户要求提交时提交本任务文件。

### Task 4: 禁止 Debug 运行数据覆盖源码

**Files:**
- Create: `BlueSapphire.Tests/DevLogSourceIsolationTests.cs`
- Modify: `Services/DevLogDataService.cs`

**Interfaces:**
- Produces: 开发日志只写 `DataFilePath` 和其本地备份。
- Removes: `TryGetProjectAssetPath` 与 Debug `File.Copy` 反向同步。

- [ ] 新增源码不变量测试，断言 `DevLogDataService` 不包含项目资源反向同步方法或日志文本。
- [ ] 运行测试，确认当前源代码包含反向同步而失败，且测试本身不写资源文件。
- [ ] 删除 Debug 反向同步代码和无用路径搜索方法。
- [ ] 运行开发日志相关测试并核对 `Assets/DevMatrixLog.json` 哈希在测试前后保持不变。
- [ ] 仅在用户要求提交时提交本任务文件。

### Task 5: 全量验证与项目事实文档

**Files:**
- Modify: `docs/cleaner-functional-audit.md`
- Modify: `docs/cleaner-workflow-acceptance.md`

**Interfaces:**
- Consumes: 本轮测试、构建、运行和关闭证据。
- Produces: 不再把未经真实验证的 P0 场景标成完成的验收记录。

- [ ] 运行聚焦测试与完整测试，记录通过数量并二次核对开发日志哈希。
- [ ] 运行 x64 Release 无增量解决方案构建，确认零警告零错误。
- [ ] 启动 `--tool=CleanerAssistant`，确认窗口句柄非零且 `Responding=True`；正常关闭并确认进程退出。
- [ ] 根据真实结果更新两份清理事实文档，不改 `Assets/DevMatrixLog.json`。
- [ ] 检查最终 diff 只包含本任务相关变化，不提交用户的其他修改。
---

## 2026-07-22 执行复核

旧清单保留原始 TDD 顺序，不把未在本轮重新观察的 RED 步骤伪装成已执行。当前真实状态如下：

| 原任务 | 当前状态 | 直接证据 |
|---|---|---|
| 自动保洁只执行可恢复动作 | 完成 | `AutomaticCleanup_LeavesLowRiskPermanentItemsForManualReview` 已存在；Cleaner 聚焦测试 101/101 通过 |
| 统一操作协调器 | Cleaner 核心完成；AI Provider 提交延后 | 协调器/ViewModel 测试 23 项通过；同名协调器互斥测试通过；Provider 在完整工作树 2/2 通过 |
| 取消后保存部分恢复、清空和重试结果 | 完成 | 三个部分完成后取消的真实文件系统测试均通过 |
| 禁止 Debug 运行数据覆盖源码 | 完成 | 开发日志历史保护提交已完成；全量测试前后源码 SHA-256 不变 |
| 全量验证与事实文档 | 完成 | Cleaner 101/101；完整工作树 216/216；Cleaner 独立快照 205/205；Debug 与 Release 构建均 0 警告、0 错误；Release 窗口正常启动和关闭 |

### 未重新制造的 RED 证据

下列步骤对应的实现已经存在于接手时的用户工作树，本轮没有回退正确实现来人为制造失败：

- 自动保洁错误删除永久项；
- 协调器类型缺失；
- 取消后历史仍保留旧状态；
- `DevLogDataService` 仍包含 Debug 源码反写。

本轮使用当前 diff、直接回归测试和隔离工作树编译错误作为证据。实际观察到的依赖失败包括：

1. 扫描切片缺少 `CleanerExecutionService.CurrentAccountingVersion`；
2. ViewModel 切片缺少页面对新版交互接口的实现；
3. 页面切片缺少新版 `DonutChart` 统计签名。

这些依赖按编译证据合并，最终候选闭包均独立测试和构建通过。