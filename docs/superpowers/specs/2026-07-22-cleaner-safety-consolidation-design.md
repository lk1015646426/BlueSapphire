# BlueSapphire Cleaner 安全重构收口设计

日期：2026-07-22

阶段：第二阶段

状态：待用户书面审阅

## 1. 背景与当前证据

第一阶段已经恢复 `Assets/DevMatrixLog.json` 正式历史、移除运行时向源码反写路径，并隔离 `Output/` 安装包生成物。当前本地分支在 `origin/master` 之上保留了独立保护检查点，其他用户重构未被覆盖。

Cleaner 当前工作区状态：

- 32 个已跟踪 Cleaner 文件发生修改，约 4,285 行新增、545 行删除。
- 11 个 Cleaner 相关核心源码、测试或事实文档仍未被 Git 跟踪。
- 当前工作树中的 Cleaner 聚焦测试为 101/101 通过。
- 当前全量测试基线为 216/216 通过。
- `docs/superpowers/plans/2026-07-19-cleaner-p0-safety.md` 中的执行清单尚未按真实结果回填。
- 当前整个脏工作树能够构建，但这不能证明仅提交 Cleaner 变更后仍能独立构建，因为 AI、UI 和通用基础设施修改可能形成隐藏依赖。

本阶段先收口现有 Cleaner 安全重构，不同时拆分 `CleanerScanService` 和 `CleanerExecutionService`，也不重新设计 Cleaner UI。

## 2. 目标

1. 将现有 Cleaner 安全重构整理为依赖闭合、可审查、可回滚的 Git 检查点。
2. 重新验证扫描范围、风险基线、执行方式、空间口径、取消、恢复、重试、自动保洁和共享操作互斥。
3. 确保所有 Cleaner 核心新增文件和测试进入 Git 跟踪，不依赖当前工作树中的偶然未跟踪文件。
4. 使用临时隔离工作树验证每个候选提交的依赖闭包，而不是仅在当前脏工作树中测试。
5. 保持 AI 为建议和调用入口，不能绕过 Cleaner 风险、边界、确认、执行方式或共享操作门禁。
6. 更新事实文档中的测试数量和验收证据，但不修改 `Assets/DevMatrixLog.json`。
7. 完成 Cleaner 聚焦测试、全量测试、零警告构建及真实 WinUI Cleaner 启动和正常关闭验证。

## 3. 非目标

本阶段不执行：

- 不拆分或重写 `Services/CleanerScanService.cs`。
- 不拆分或重写 `Services/CleanerExecutionService.cs`。
- 不增加新的内置扫描规则或扩大扫描目录。
- 不扩大默认选中、自动清理或永久删除范围。
- 不重新设计 Cleaner 页面视觉和信息架构。
- 不完成通用 AI Provider 架构收口；只包含 Cleaner 安全接线所必需的最小文件。
- 不整理媒体工作台、主题系统或其他页面重构。
- 不批量规范整个仓库的行尾或格式。
- 不发布安装包或更新版本号。

## 4. 安全不变量

### 4.1 操作互斥

任一时刻只允许一个 Cleaner 主操作持有共享门禁：

- 扫描；
- 手动清理；
- 自动保洁；
- 失败重试；
- 恢复；
- 清空隔离区；
- AI 发起的扫描或清理。

所有入口必须共享同一个 `CleanerOperationCoordinator` 实例或同名系统级同步对象。按钮可用状态必须反映真实门禁状态，不能显示可点击后静默返回。

### 4.2 扫描范围

- 每次扫描开始时只快照一次选中的驱动器根路径。
- 扫描输入、扩展扫描、去重指纹、审计和 UI 摘要使用同一个快照。
- `C:\` 不得被截断为 `C:` 后按当前工作目录解析。
- 单盘扫描不能返回其他盘对象；多盘扫描不能偷偷回退到系统盘。
- 新扫描开始后不能混合上一次报告；取消或失败只能恢复上一次完整报告。

### 4.3 风险与执行

- 规则声明的风险是基线，运行时证据只能在硬边界内调整。
- 用户文件、只读调查对象和高风险系统对象不得进入普通自动删除链路。
- 自动保洁只处理默认选中的低风险且实际可执行对象。
- `Permanent`、`Quarantine`、系统专用动作和只读查看必须保持不同执行路径。
- AI 不能改变风险、可选性、执行方式、驱动器范围、边界校验或用户确认。

### 4.4 空间口径

必须区分：

- `DetectedBytes`：扫描发现字节；
- `ProcessedBytes`：已处理对象字节；
- `ReleasedBytes`：实际释放磁盘空间；
- `RecoverableBytes`：移入隔离区、仍占用磁盘的可恢复数据。

UI、AI、审计和遥测不得把 `RecoverableBytes` 描述为已释放空间。

### 4.5 取消与失败

- 所有长操作必须传播 `CancellationToken`。
- 取消必须表现为取消，不能显示 100% 完成。
- 恢复、清空隔离区和失败重试在部分完成后被取消时，必须保存已经发生的批次结果和历史。
- 清理完成、失败或取消后必须先释放操作门禁，再触发刷新。
- 关闭窗口不得在 UI 线程同步等待后台任务。

## 5. 收口切片

### 切片 A：规则、模型、路径和扫描范围

职责：

- Cleaner 数据模型和统计口径；
- 内置规则及规则版本；
- 风险基线；
- 路径边界和驱动器根路径；
- 应用安装发现和已知路径解析；
- 扫描范围快照、规则扩展和结果去重；
- 状态存储迁移。

主要候选文件：

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

### 切片 B：执行、系统动作、恢复和空间统计

职责：

- 永久删除、隔离和系统专用动作；
- Windows 系统清理执行器；
- 实际释放与可恢复空间统计；
- 清理审计；
- 恢复、失败重试和清空隔离区；
- 部分完成后取消的持久化。

主要候选文件：

- `Services/CleanerExecutionService.cs`
- `Services/CleanerSystemCleanupService.cs`
- `Services/CleanerAuditService.cs`
- `BlueSapphire.Tests/CleanerExecutionServiceTests.cs`
- `BlueSapphire.Tests/CleanerSystemCleanupServiceTests.cs`
- 依赖闭包要求的模型和状态文件。

### 切片 C：操作协调和 ViewModel 工作流

职责：

- 共享操作协调器和一次性租约；
- 扫描、清理、重试、自动保洁、恢复和清空互斥；
- 规则/排除变更后的延迟一致性刷新；
- 命令可用状态；
- 扫描取消后恢复上一份完整报告；
- Cleaner 设置、自动化和遥测状态同步。

主要候选文件：

- `Services/CleanerOperationCoordinator.cs`
- `ViewModels/Cleaner/*.cs`
- `ViewModels/CleanerAssistantViewModel.cs`
- `ViewModels/CleanerAssistantViewModel.Properties.cs`
- `ViewModels/CleanerSettingsViewModel.cs`
- `Interfaces/ICleanerAssistantViewInteraction.cs`
- 对应 ViewModel、协调器和 P0 验收测试。

### 切片 D：Cleaner UI、安全 AI 接线和事实文档

职责：

- Cleaner 页面绑定和命令可用状态；
- 扫描、确认、执行、取消、结果和恢复的可见状态；
- AI 发起 Cleaner 操作时复用共享门禁和结构化证据；
- 事实文档、验收矩阵和真实验证结果。

主要候选文件：

- `CleanerAssistantPage.xaml`
- `CleanerAssistantPage.xaml.cs`
- `Services/CleanerAIToolActionProvider.cs` 及其测试，仅限安全接线所需内容；
- `docs/cleaner-functional-audit.md`
- `docs/cleaner-workflow-acceptance.md`
- 2026-07-19 P0 设计和实施记录。

如果 `CleanerAIToolActionProvider` 依赖尚未提交的通用 AI 能力接口，则本切片只能选择以下一种方式：

1. 将最小通用接口依赖一并纳入，并证明不会带入 Media/UI 行为；或
2. 暂不提交 AI Provider，但保留明确的依赖阻塞记录，由后续 AI 架构阶段提交。

不得复制一套 Cleaner 专用通用 AI 接口来规避依赖。

## 6. 依赖闭包验证

### 6.1 原则

当前工作树包含大量未提交 AI 和 UI 修改，因此“当前工作树测试通过”不能证明候选 Cleaner 提交完整。

每个切片在提交前必须：

1. 从当前 `HEAD` 创建临时隔离工作树。
2. 只应用该切片候选补丁和明确列出的新增文件。
3. 检查 `git status`，确认没有从主工作树泄漏其他修改。
4. 在隔离工作树中恢复依赖并运行相关测试、全量测试或至少完整构建。
5. 如果编译揭示缺失依赖，只根据编译和类型引用证据扩大闭包。
6. 隔离验证完成后正常删除临时工作树，不删除主工作区文件。

### 6.2 闭包失败处理

- 缺失模型或接口：将真正共享且必需的文件纳入当前切片。
- 依赖另一独立产品重构：停止扩大，重新调整切片边界。
- 只有当前工作树能通过：视为候选提交不完整，不允许提交。
- 测试只在 Debug 输出偶然通过：重新执行无增量构建和受控文件系统测试。

## 7. 测试驱动和验收切片

| 用户动作 | 期望状态 | 责任模块 | 自动化证明 | 真实证明 |
|---|---|---|---|---|
| 首次进入 Cleaner | 驱动器加载完成且至少选中一个 | Drive VM/Service | ViewModel 测试 | 真实窗口 |
| 单盘扫描 | 只返回所选盘对象 | Scan VM/Service | 受控文件系统测试 | 测试目录扫描 |
| 多盘扫描 | 合并所选盘且不回退系统盘 | Scan VM/Service | 多根路径测试 | 可用测试盘时验证 |
| 重复扫描后取消 | 恢复上一份完整报告 | Scan VM | 状态机测试 | 真实窗口取消 |
| 手动清理 | 永久、隔离、系统动作分别统计 | Execution/VM | 真实文件测试 | 确认摘要 |
| 自动保洁 | 只处理默认低风险可执行项 | Automation/Execution | 自动保洁回归测试 | 测试目录 |
| 失败重试 | 释放自身门禁后刷新 | Assistant VM | 状态机测试 | 失败模拟 |
| 恢复中取消 | 保存已恢复批次结果 | Execution/Cleanup VM | 真实文件取消测试 | 测试隔离区 |
| 清空中取消 | 保存已清空部分的历史 | Execution/Cleanup VM | 真实文件取消测试 | 测试隔离区 |
| 扫描中恢复 | 恢复入口不可用 | Coordinator/UI | Coordinator + XAML 编译 | 真实窗口 |
| AI 扫描/清理 | 共享门禁且不绕过风险 | AI Provider/Coordinator | Provider 测试 | AI 入口冒烟 |
| 关闭窗口 | 取消后台任务并正常退出 | Page/MainWindow | 生命周期测试 | 正常关闭 |

发现缺口时使用最小 RED→GREEN 修复：

1. 增加用户可见失败的最小测试。
2. 运行并确认按预期失败。
3. 只修改根因代码。
4. 运行聚焦测试和最近子系统测试。
5. 绿色后才允许整理代码或形成检查点。

## 8. Git 检查点策略

- 每个切片只暂存明确列出的文件。
- 提交前检查 `git diff --cached --name-only` 和 `git diff --cached --check`。
- 不执行 `git add .`。
- 不将 Cleaner 之外的主题、媒体或通用 UI 重构混入。
- 如果切片之间无法在中间状态独立构建，允许合并相邻切片，但必须在设计记录中说明编译依赖证据。
- 每个检查点提交后再次运行最接近的测试。

建议提交语义：

1. `fix: 收口 Cleaner 扫描范围与风险规则`
2. `fix: 收口 Cleaner 执行恢复与空间统计`
3. `fix: 统一 Cleaner 操作协调和工作流状态`
4. `fix: 完成 Cleaner 安全界面与验收接线`
5. `docs: 更新 Cleaner 真实验收记录`

## 9. 失败、取消与回滚

- 如果隔离工作树构建失败，不修改主工作树来隐藏失败，先记录缺失依赖。
- 如果三个连续修复暴露不同的共享状态所有权问题，停止叠加补丁，重新讨论状态架构。
- 如果真实文件测试可能触及用户目录，必须改用受控临时目录；不得为了“真实”而扫描或删除用户数据。
- 如果系统清理需要管理员权限，本阶段自动验证只检查计划、边界和命令构造，不执行真实 Windows Update 或系统目录删除。
- 回滚只针对当前切片明确修改的文件，不覆盖其他用户重构。
- `Assets/DevMatrixLog.json` 在本阶段必须保持哈希不变。

## 10. 完成标准

本阶段只有同时满足以下条件才算完成：

1. 所有纳入范围的 Cleaner 核心新增源码和测试已经被 Git 跟踪。
2. 候选提交经过隔离工作树依赖闭包验证。
3. Cleaner 聚焦测试不少于当前 101 项且零失败。
4. 全量测试不少于当前 216 项且零失败。
5. 无增量解决方案构建为 0 警告、0 错误。
6. 单盘/多盘范围、重复扫描取消、自动保洁、恢复/清空取消、失败重试和共享互斥都有直接证据。
7. 永久、隔离、系统动作、实际释放和可恢复字节口径保持分离。
8. AI Cleaner 入口要么完成共享安全接线，要么被明确留给后续 AI 阶段且不影响 Cleaner 核心提交完整性。
9. 真实 x64 Cleaner 窗口获得非零句柄、`Responding=True`，正常关闭后进程退出。
10. 两份 Cleaner 事实文档与当前测试、构建和真实验证结果一致。
11. `Assets/DevMatrixLog.json` 哈希在阶段前后保持不变。
12. Cleaner 阶段提交不包含媒体、主题或无关 UI 重构。
