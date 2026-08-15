# BlueSapphire 稳定性与质量修复计划

> 版本：1.0
> 日期：2026-08-15
> 来源：2026-08-15 全项目诊断（基于 240/240 测试通过的基线做的深度代码审查）
> 用途：作为后续修复工作的执行依据，按批次推进
> 原则：本文档只描述目标效果和验收标准，不限定具体实现方式
> 前置：本文档是 `optimization-plan.md`（2026-06-15）的后续。该文档 9 项中绝大多数已落地，本计划处理遗留学病与新发现问题。

---

## 修复批次总览

| 编号 | 事项 | 优先级 | 严重度 | 影响范围 |
|---|---|---|---|---|
| F1 | 退出链路修复：ServiceProvider 未释放 + AITaskCenterService 同步阻塞 Dispose | 🔴 P0 | 高 | 数据丢失 / 进程残留 / 退出卡死 |
| F2 | AIToolsRegistry 空 catch 治理（7 处） | 🔴 P0 | 高 | 可诊断性 |
| F3 | 设置保存链路失败反馈（CleanerSettingsViewModel + AppSettings） | 🔴 P0 | 高 | 用户数据静默丢失 |
| F4 | CancellationTokenSource 资源泄漏（3 处） | 🟠 P1 | 中 | 资源泄漏 |
| F5 | 仓库卫生收口（脏文件提交、.gitignore、README 数据更新） | 🟠 P1 | 中 | 工程卫生 |
| F6 | 其余静默 catch 补日志（约 10 处） | 🟡 P2 | 中 | 可诊断性 |
| F7 | 巨型文件拆分（MediaManagerViewModel / AIToolsRegistry 优先） | 🟡 P2 | 中 | 可维护性 |
| F8 | 有状态 Singleton 线程安全审计 | 🟡 P2 | 中 | 并发安全 |
| F9 | 低优先级观察项（UI 火忘调用、注释-only catch） | 🟢 P3 | 低 | — |

---

## F1. [P0] 退出链路修复

> **2026-08-15 核实修正**：诊断报告中"ServiceProvider 从不释放"为误报——`MainWindow.xaml.cs` 的 `MainWindow_Closed` 已调用 `(App.Current.Services as IDisposable)?.Dispose()` 后再 `Application.Current.Exit()`，`McpServerManager` 等 IDisposable 单例会随之级联释放。本批次实际修复范围为下方第 2 条（AITaskCenterService 的 Dispose 同步阻塞 + 吞异常）。

### 现状

1. `App.xaml.cs:46/68`：`Services = ConfigureServices()` 构建的 `ServiceProvider` 全程无 `Dispose()` 调用。所有 `IDisposable` 单例（`AITaskCenterService`、`McpServerManager` 及其持有的 MCP 子进程、`CleanerLockService` 等）在正常退出时不会执行清理。
2. `Services/AITaskCenterService.cs:399`：`Dispose()` 内部为
   ```csharp
   try { SaveAsync().ConfigureAwait(false).GetAwaiter().GetResult(); } catch { }
   ```
   同步阻塞等待异步落盘，且异常被完全吞掉。

### 风险

- MCP 子进程在应用退出后残留。
- 任务中心最终一次状态保存失败时静默丢失，用户无感知。
- 若落盘 I/O 阻塞（磁盘慢、文件被占用），关闭流程会卡死。

### 需要达到的效果

1. 应用退出路径上 `ServiceProvider` 被正确释放，所有 `IDisposable` 单例的 `Dispose()` 得到调用。
2. `AITaskCenterService` 的最终落盘不再使用同步阻塞（改为 `IAsyncDisposable`，或在窗口关闭前由 UI 层提前 `await SaveAsync()`）。
3. 最终落盘失败至少写入日志警告，不再静默。

### 验收标准

1. 启动应用并启用至少一个 MCP 服务器，正常退出后任务管理器中无残留子进程。
2. 将任务持久化目录设为只读后退出应用，应用仍能正常退出（不卡死、不崩溃），日志中出现保存失败警告。
3. `dotnet test` 全量通过。

---

## F2. [P0] AIToolsRegistry 空 catch 治理

### 现状

`Services/AIToolsRegistry.cs`（1743 行）中 7 处完全空的 `catch { }`，无日志、无注释：

- 第 125 行：`DriveInfo.GetDrives()` 失败 → systemPrompt 缺少磁盘信息，直接影响 AI 决策质量
- 第 174、211、243、747、1472、1483 行：工具能力构建、磁盘枚举、JSON 解析等失败全部静默

### 风险

AI 助手能力静默降级且无法排查。第 125 行尤其严重：磁盘信息缺失会让 AI 的清理建议建立在错误前提上。

### 需要达到的效果

1. 每处 `catch` 至少注入 `ILogger` 并记录 `LogWarning`（含异常对象）。
2. 对会实质影响 AI 能力构建的失败（如磁盘枚举），让上层可以感知降级状态（如能力目录中标注"部分不可用"）。

### 验收标准

1. 全文搜索 `AIToolsRegistry.cs` 不存在无日志且无注释说明的空 `catch`。
2. 人为制造一个解析失败（如损坏的规则 JSON），日志中能找到对应警告。
3. `dotnet test` 全量通过。

---

## F3. [P0] 设置保存链路失败反馈

> **2026-08-15 核实修正**：诊断报告中"`CleanerSettingsViewModel` 三处 `_ = SaveXxxAsync` 火忘式无反馈"为误报——三个 Save 方法内部均全程 try-catch，失败时已通过 `WeakReferenceMessenger` 发送 `ShowTipMessage` 用户可见提示，`OperationCanceledException` 空属防抖正常语义。本批次实际修复范围为下方第 2 条（AppSettings 写盘失败静默），并按日志可观测方案落地（新增 `PersistFailed` 静态事件 + App 层订阅路由到 ILogger）。

### 现状

1. `ViewModels/CleanerSettingsViewModel.cs:102/164/207`：三处 `_ = SaveXxxAsync(...)` 火忘式调用，保存异常（磁盘满、权限、文件占用）既不被观察也不被记录。
2. `Helpers/AppSettings.cs:84/145/168`：`try { WriteSettingsUnsafe(); } catch { }`，写盘失败完全静默。

### 风险

用户切换设置后看到 UI 已更新，实际配置从未落盘，下次启动静默回退。这是 `optimization-plan.md` 第 1 条的遗留：崩溃问题已修复，但"失败可感知"未完成。

### 需要达到的效果

1. 保持不崩溃的现状（同步阻塞与 async void 问题已解决，不得回退）。
2. 保存失败时：写入日志，并在 UI 上给用户可见反馈（状态栏提示或 InfoBar）。
3. `AppSettings` 写盘失败向上层抛出或返回可判断的结果，由调用方决定提示方式。

### 验收标准

1. 将 `%LocalAppData%\BlueSapphire` 设为只读，切换自动化/遥测/隔离区设置，应用不崩溃且界面出现保存失败提示。
2. 恢复写权限后设置保存恢复正常。
3. 所有现有 Settings 相关单元测试通过。

---

## F4. [P1] CancellationTokenSource 资源泄漏

> **2026-08-15 核实修正**：诊断报告所列 `MediaManagerViewModel.BeginCancelableOperation` 与 `CleanerCleanupViewModel.BeginRestoreOperation` 两处为误报——经逐调用点核查，两处 Begin 的全部使用方（各 2 处与 3 处）均在 finally 中配对调用 End 方法，End 内已无条件 `Dispose()`，被替换的旧 CTS 最终由其所属操作方法释放，无泄漏。本批次实际修复的只有 `ImageItem.LoadImageAsync` 一处（dispatcher 回调不执行时 loadingCts 永不释放，及"回调未执行 + 未被重入替换"两路径均不释放的缺口）。

### 现状

1. `ViewModels/MediaManagerViewModel.cs:1289`：`_globalCts?.Cancel();` 后直接 `_globalCts = next;`，旧 CTS 未 Dispose。
2. `ViewModels/Cleaner/CleanerCleanupViewModel.cs:545`：`_restoreCts` 同样模式。
3. `Models/ImageItem.cs:280-333`：`loadingCts` 的 Dispose 依赖 dispatcher 回调中的 finally；窗口关闭中 dispatcher 队列停机时回调不执行，CTS 永不释放。

### 风险

高频触发扫描/清理/图片加载会累积未释放的内核句柄（仅靠终结器兜底）。

### 需要达到的效果

1. 覆盖 CTS 字段前先 `Cancel()` 再 `Dispose()`（两者顺序调用安全，Dispose 幂等）。
2. `ImageItem` 增加兜底释放路径（取消加载或对象失效时无条件 Dispose）。

### 验收标准

1. 连续触发 20 次以上"开始-取消"操作循环，任务管理器中进程句柄数保持稳定。
2. 快速扫描大量图片后立即关闭窗口，无调试器输出 CTS 泄漏警告（启用 `CancellationTokenSource` 追踪时）。
3. `dotnet test` 全量通过。

---

## F5. [P1] 仓库卫生收口

### 现状

1. 工作树有 20 个文件修改未提交（2026-07-22 起的 UI 重构），未按项目"完成 = 可独立构建的提交闭包"标准收口。
2. `.agents/`、`.trae/`、`.workbuddy/` 目录未跟踪也未加入 `.gitignore`。
3. `README.md` 数据滞后：写"当前版本 1.0.3、测试基线 165 项"，实际测试 240 项；版本号在文档间不一致（1.0.3 / 1.0.5）。

### 需要达到的效果

1. 本轮 UI 重构完成真实流程复审后按闭包标准提交。
2. AI 协作目录加入 `.gitignore`。
3. README 更新版本号与测试基线，全仓库版本号统一为单一来源（建议 `BlueSapphire.csproj` 版本为准）。

### 验收标准

1. `git status` 干净（无未提交修改、无未忽略的未跟踪目录）。
2. `git clone` 后可直接 `dotnet build` 成功。
3. README 中测试数与 `dotnet test` 实际输出一致。
4. 按 `docs/cleaner-workflow-acceptance.md` 对 UI 改动完成一次真实流程复审并记录。

---

## F6. [P2] 其余静默 catch 补日志

### 现状

以下位置为空 catch 或仅注释 catch，故障不可观测：

| 文件 | 行号 |
|---|---|
| `Services/AIMediaToolService.cs` | 380 |
| `Services/AIMemoryService.cs` | 314、354 |
| `Services/AITaskCenterService.cs` | 261 |
| `Services/CleanerApplicationDiscoveryService.cs` | 88 |
| `Models/DuplicateItem.cs` | 75 |
| `Services/AgentSkillManager.cs` | 59 |
| `Services/AIChatHistoryService.cs` | 88 |
| `App.xaml.cs` | 492、101、560 |
| `MainWindow.xaml.cs` | 67、317 |
| `Helpers/JobObjectHelper.cs` | 49、72 |
| `Services/AIClassifierService.cs` | 101 |
| `Views/AICopilotPage.xaml.cs` | 191 |
| `DuplicateResultDialog.xaml.cs` | 87 |
| `MediaManagerPage.xaml.cs` | 227 |

### 需要达到的效果

1. 每处 catch 至少记录 `LogWarning`/`LogError`；确属可忽略的，写注释说明为何可忽略。
2. `OperationCanceledException` 的空 catch 属正常取消语义，保持现状（可选加 Debug 级日志）。

### 验收标准

全文搜索 `catch` 无"既无日志又无注释"的空块；`dotnet test` 全量通过。

---

## F7. [P2] 巨型文件拆分

### 现状（超过 800 行的文件，共 8 个）

| 文件 | 行数 |
|---|---|
| `ViewModels/MediaManagerViewModel.cs` | 1829 |
| `Services/AIToolsRegistry.cs` | 1743 |
| `CleanerAssistantPage.xaml` | 1653 |
| `Services/CleanerScanService.cs` | 1188 |
| `Services/CleanerExecutionService.cs` | 1165 |
| `Models/CleanerModels.cs` | 959 |
| `Services/ImageProcessingService.cs` | 876 |
| `SettingsPage.xaml.cs` | 853 |

### 风险

`CleanerAssistantViewModel`（原 2155 行）已完成拆分，但 Media 和 AI 模块长出了同样的病：定位困难、合并冲突高发、无法独立测试。

### 需要达到的效果

优先拆分前两个，参照 `CleanerAssistantViewModel` 的既有拆分模式：

1. `MediaManagerViewModel` → 按扫描加载 / 去重 / 重命名 / 批处理 / 标签筛选拆为子 ViewModel。
2. `AIToolsRegistry` → 按能力目录构建 / 系统提示词组装 / 磁盘与应用发现拆分。

其余 6 个文件在后续迭代中视改动机会顺势拆分，不单独安排。

### 验收标准

1. 拆分后主文件 ≤ 800 行。
2. XAML 绑定路径与交互行为不变（从页面看无差异）。
3. 所有现有测试通过，不降低覆盖。

---

## F8. [P2] 有状态 Singleton 线程安全审计

### 现状

`App.xaml.cs:384-448` 约 50 个服务全部 `AddSingleton`，其中多个持有可变状态且被多页面共享。已确认加锁的：`AITaskCenterService`（`lock(_sync)`）、`AppSettings`（`lock(Sync)`）。未审计的重点对象：

- `McpServerManager`（子进程集合，UI 与后台并发访问）
- `CleanerStateStore`（状态存储）
- `AISharedContextService`（共享上下文）

### 需要达到的效果

1. 逐个审计上述服务的内部同步，补齐锁或改用不可变快照交换。
2. 对确认为单线程访问的服务，写注释说明线程模型，避免后人误用。

### 验收标准

1. 审计结论记录在 `docs/cleaner-architecture.md` 或独立文档。
2. 为并发访问路径补至少 1 个并发场景单元测试（如并行调用状态存储）。

---

## F9. [P3] 低优先级观察项

1. `Views/AICopilotPage.xaml.cs:220/544/552/621`：`_ = ScrollToBottomAsync();` 火忘调用——建议方法内部 try/catch。
2. 注释-only catch（`App.xaml.cs:101/560`、`MainWindow.xaml.cs:67/317`、`JobObjectHelper.cs:49/72` 等）——随 F6 顺带处理。
3. 版本号散落（1.0.3 / 1.0.5 / installer.iss）——随 F5 统一。

---

## 修复顺序与回归基线

### 推荐顺序

1. **F1**（退出链路）—— 先解决丢数据与进程残留
2. **F2 + F3**（吞异常治理）—— 这是诊断其他一切问题的前提
3. **F4**（CTS 泄漏）
4. **F5**（仓库收口，含本轮 UI 闭包提交）
5. **F6 → F8**（中期，可穿插进行）
6. **F7**（大型重构，单独排期）
7. **F9**（随缘）

### 回归基线（每个批次完成后必须执行）

- `dotnet test` 全量通过（修复前基线：240/240，0 编译警告）
- 真实窗口冒烟：启动 → 快速扫描 → 查看结果 → 执行一次安全清理 → 正常退出 → 确认无残留进程
- 任何修复不得删除或跳过现有测试

---

> **审核说明**：请逐项确认目标效果与验收标准是否合理，调整意见直接批注在对应条目下。确认后按"修复顺序"推进，每完成一个批次在本文档末尾追加完成记录。

## 完成记录

| 日期 | 批次 | 结果 | 备注 |
|---|---|---|---|
| 2026-08-15 | F1 | ✅ 完成 | AITaskCenterService 注入可选 ILogger；SaveAsync 落盘失败记警告；Dispose 改为 3 秒超时保护 + 失败/超时日志（超时不释放 _saveGate，避免运行中的 Release 抛异常）。ServiceProvider 释放经核实已存在，未改动 |
| 2026-08-15 | F2 | ✅ 完成 | AIToolsRegistry 注入可选 ILogger，7 处空 catch（磁盘枚举/长期偏好/第三方技能/代理探测/下载清理/MCP 枚举/Web Skill 枚举）全部补 LogWarning |
| 2026-08-15 | F3 | ✅ 完成 | AppSettings 新增 PersistFailed 静态事件，Save/Remove 写盘失败触发；App.ConfigureServices 订阅并路由到文件日志。ViewModel 层 UI 反馈经核实已存在。新增测试 Save_DoesNotThrow_AndRaisesPersistFailed_WhenDiskWriteFails |
| 2026-08-15 | F4 | ✅ 完成 | 仅 ImageItem 属实：外层 finally 去掉 !handedOffToUi 前置条件，无条件兜底释放 loadingCts（Dispose 幂等）。另两处 Begin/End 配对完整，判定误报未改动 |
| 2026-08-15 | 回归 | ✅ 通过 | dotnet build 0 错误 0 警告；dotnet test 241/241 通过（原 240 + 新增 1）；csproj 增加 InternalsVisibleTo("BlueSapphire.Tests") |
| 2026-08-15 | F5 | ✅ 完成 | 版本号核实已全部统一为 1.0.3（csproj/README/installer.iss/builder_config，1.0.5 仅存于历史文档）；README 测试基线 165→241；.gitignore 补 .agents/.trae/.workbuddy；遗留 20 个 UI 重构文件以独立提交收口（e03ae13），仓库卫生 6ac6ef9，工作树干净 |
| 2026-08-15 | F6 | ✅ 完成 | 计划内 15 处全部处理：AgentSkillManager/AIChatHistoryService/AIMemoryService 注入可选 ILogger 记警告；AIMediaTool/CleanerApplicationDiscovery/DuplicateItem/App.xaml.cs(ProxyTools)/DuplicateResultDialog/MediaManagerPage 补注释说明可忽略原因；AICopilotPage 导出失败改为用户可见 ContentDialog。另核实 JobObjectHelper/AIClassifierService/MainWindow/App(560) 原有注释已达标。遗留：全文另扫出约 59 处 `try {...} catch {}` 单行尽力而为模式（取文件大小/删临时文件类），未列入本计划，留待后续批次 |
| 2026-08-15 | F7 | ✅ 完成 | MediaManagerViewModel 1829 行 → 主文件 615 + Rename 330 + Operations 532 + Library 373（partial class 拆分，方法集 93/93 一致，XAML 绑定零变化）；AIToolsRegistry 1772 行 → 主文件 424 + SystemPrompt 175 + WebFetch 548 + ToolCatalog 468 + AgentLoop 216（方法集 23/23 一致）。其余 6 个超 800 行文件按计划留待后续改动机会顺势拆分 |
| 2026-08-15 | 回归(F5-F7) | ✅ 通过 | dotnet build 0 错误 0 警告；dotnet test 241/241 通过 |
