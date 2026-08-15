# BlueSapphire 优化需求文档

> 版本：1.0  
> 日期：2026-06-15  
> 用途：供开发团队审核和后续实现参考  
> 原则：本文档仅描述需要达到的目标效果和验收标准，不涉及具体实现方式

---

## 1. [P0] 消除非事件处理器中的 async void

### 1.1 现状

项目中共有 12 处 `async void` 方法，其中 10 处是 UI 事件处理器（如 `Page_Loaded`、`Button_Click`、`DoubleTapped`），符合 WinUI 3 框架要求，无需修改。

但有 2 处位于 `ViewModels/CleanerSettingsViewModel.cs`，是通过 CommunityToolkit.Mvvm 的 `partial void OnXxxChanged` 触发的内部逻辑：

| 方法 | 行号 | 触发场景 |
|---|---|---|
| `SaveAutomationSettings()` | 78 | 自动化设置的任一属性变更时 |
| `SaveTelemetrySettings()` | 88 | 遥测开关变更时 |

这两个方法不是 UI 事件处理器，而是由属性变更通知链触发的业务逻辑。

### 1.2 风险

`async void` 方法中抛出的异常无法被任何 `try-catch` 捕获，会直接导致整个进程崩溃（白屏闪退）。`SaveAutomationSettings` 和 `SaveTelemetrySettings` 内部都涉及文件 I/O 操作，存在因磁盘满、权限不足、文件被占用等原因失败的可能。

### 1.3 需要达到的效果

1. `SaveAutomationSettings` 和 `SaveTelemetrySettings` 的异常可以被上层捕获和处理，不会导致进程崩溃。
2. 当保存失败时，用户界面应有可感知的反馈（如状态栏提示保存失败），而非静默丢失设置或直接闪退。
3. 调用链上层的 `partial void OnXxxChanged` 方法签名保持不变（CommunityToolkit.Mvvm 源码生成器的要求）。
4. 修改后的行为与当前行为一致：属性变更时自动触发保存。

### 1.4 验收标准

- 模拟磁盘写入失败场景（如将配置文件目录设为只读），切换自动化或遥测开关，应用程序不崩溃。
- 保存失败后，界面上出现用户可见的错误提示。
- 所有现有的 Settings 相关单元测试通过。

---

## 2. [P1] 拆分 CleanerAssistantViewModel（2155 行 → ≤800 行主文件）

### 2.1 现状

`ViewModels/CleanerAssistantViewModel.cs` 包含 2155 行代码，约 90 个方法，承载了以下全部职责：

- 扫描触发与进度管理
- 扫描结果分桶（Safe / Review / ViewOnly）
- 清理执行与进度管理
- 隔离恢复
- 排除项管理
- 磁盘选项管理
- 自动化保洁调度
- 规则包导入/更新/清除
- 发布通道切换（Stable / Canary / Internal）
- 遥测上传
- 驱动选择持久化
- 诊断报告导出
- Profile 状态管理
- 提权模式进入与失败重试

### 2.2 风险

单一文件的巨型 ViewModel 导致以下问题：

1. 任何修改都需要在 2000+ 行中定位，合并冲突概率极高。
2. 职责混杂，无法单独测试某一模块（如自动化调度 vs 扫描流程）。
3. UI 绑定的属性（约 60+ 个公共属性）和业务方法堆在一起，阅读困难。
4. 多个开发者无法并行修改不同功能模块。

### 2.3 需要达到的效果

将当前单体 ViewModel 按功能边界拆分为多个子 ViewModel，原 ViewModel 仅保留顶层协调逻辑。建议拆分方向如下（最终拆分方案由开发团队讨论确定）：

| 子 ViewModel（建议） | 负责范围 | 预估行数 |
|---|---|---|
| `CleanerScanViewModel` | 扫描触发、进度、取消、结果分桶展示 | ≤400 |
| `CleanerCleanupViewModel` | 清理执行、进度、隔离/恢复/重试 | ≤350 |
| `CleanerAutomationViewModel` | 自动化调度、提醒间隔、自动保洁触发 | ≤200 |
| `CleanerRuleManagementViewModel` | 规则包导入/更新/清除、发布通道切换 | ≤200 |
| `CleanerDriveSelectionViewModel` | 磁盘选项、驱动选择持久化 | ≤150 |
| `CleanerAssistantViewModel`（主） | 持有上述子 VM、协调跨模块操作、对外暴露汇总属性 | ≤400 |

### 2.4 验收标准

1. 主 ViewModel 文件不超过 800 行。
2. 每个子 ViewModel 可以独立实例化（通过依赖注入），不依赖主 ViewModel 的存在。
3. 现有所有页面的 UI 绑定保持不变——从 XAML 侧看，`CleanerAssistantPage` 的 DataContext 绑定路径和交互行为与拆分前完全一致。
4. 所有现有 `CleanerAssistantViewModelTests` 和 `CleanerScanServiceTests` 通过。
5. 扫描→执行→恢复的完整用户操作流程无退化。

---

## 3. [P1] 补齐 ViewModel 层测试

### 3.1 现状

- `CleanerAssistantViewModel`（2155 行）：**零单元测试**——仅依赖集成测试间接覆盖。
- `MediaManagerViewModel`（1289 行）：测试覆盖率极低。
- Service 层测试质量尚可，但 ViewModel 层是用户行为的直接入口，选中/取消、批量操作、风险提示文案、按钮可用性等逻辑均未验证。

### 3.2 风险

ViewModel 层的 bug 直接暴露给用户：该禁用的按钮可以点击、选中项目的计数显示错误、清理执行后结果列表不刷新。这些 bug Service 层测试完全无法捕获。

### 3.3 需要达到的效果

为以下 ViewModel 的核心交互流程添加测试覆盖：

| 测试目标 | 需覆盖的关键场景 |
|---|---|
| `CleanerAssistantViewModel` | 扫描完成后结果分桶是否正确（Safe/Review/ViewOnly）；全选/取消全选；清理执行前后按钮状态变化；排除项添加/移除后列表刷新；磁盘选项变更后扫描参数更新 |
| `MediaManagerViewModel` | 图片加载后去重结果显示；重命名预览的生成与确认；格式转换的选项联动；批量操作的选中计数 |

测试应聚焦于 **用户可见的行为和状态**：按钮的 `IsEnabled`、列表的 `Count`、状态文本的变化，而非实现细节。

### 3.4 验收标准

1. `CleanerAssistantViewModel` 至少有 8 个测试方法覆盖上述关键场景。
2. `MediaManagerViewModel` 至少有 5 个测试方法覆盖上述关键场景。
3. 测试中使用真实的 Service 实例或测试替身均可，重点是验证 ViewModel 的行为正确性。
4. `dotnet test` 全量运行通过。

---

## 4. [P1] API Key 安全存储

### 4.1 现状

`Helpers/AppSettings.cs` 将 DeepSeek API Key 以明文 JSON 形式存储在：

```
%LocalAppData%\BlueSapphire\config.json
```

`Services/DeepSeekAIService.cs` 通过 `AppSettings.Get("DeepSeekApiKey", ...)` 读取。

### 4.2 风险

任何能读取该文件的程序（包括恶意软件、其他用户进程）都可以直接获取 API Key。若 Key 泄露，攻击者可以盗用配额产生账单损失。

### 4.3 需要达到的效果

1. API Key 不以明文形式存储在磁盘上。
2. 加密方案使用 Windows 平台原生机制（如 DPAPI `System.Security.Cryptography.ProtectedData`），绑定当前用户账户。其他用户或重装系统后无法解密。
3. 应用程序读取 API Key 的方式保持不变（调用方无需修改），加密/解密逻辑封装在 `AppSettings` 或一个独立的密钥存储服务中。
4. 不影响已有的非敏感设置项（如界面偏好）的读写方式。
5. 旧版本已存储的明文 Key 首次启动时自动迁移到加密存储，并清除明文。

### 4.4 验收标准

1. 在配置界面输入 API Key 并保存后，打开 `config.json` 查看，Key 的值不再是明文。
2. 应用程序正常启动后，AI Copilot 功能可以使用已保存的 Key 正常工作。
3. 将 `config.json` 复制到另一台机器或另一个 Windows 账户下，无法解密出原始 Key。
4. 从旧版本升级（已有明文 Key）首次启动后，明文 Key 从磁盘消失，AI 功能仍可用。

---

## 5. [P2] 引入 IHttpClientFactory

### 5.1 现状

`Services/DeepSeekAIService.cs` 中直接 `new HttpClient()` 创建 HTTP 客户端实例。该项目中 DeepSeekAIService 注册为 Singleton，所以当前不会立即触发问题，但：

1. 依赖 Singleton 来规避端口耗尽，是一种隐式约束——未来若有人在 DI 中改为 Transient 注册就会出问题。
2. `HttpClient` 不响应 DNS 变更（长生命周期 Singleton 的已知缺陷）。

### 5.2 需要达到的效果

1. DeepSeekAIService 通过依赖注入获取 `IHttpClientFactory` 创建的 `HttpClient` 实例。
2. `IHttpClientFactory` 在 DI 容器中正确注册（Microsoft.Extensions.Http 包）。
3. 无论 DeepSeekAIService 注册为 Singleton、Transient 还是 Scoped，都不会出现端口耗尽问题。
4. API 调用的行为（请求头、超时、响应处理）与当前完全一致。

### 5.3 验收标准

1. AI Copilot 对话功能正常发送和接收消息。
2. 即使将 DeepSeekAIService 改为 Transient 注册，连续发送 100 条消息后无异常。
3. `dotnet test` 全量通过。

---

## 6. [P2] 统一日志抽象

### 6.1 现状

项目中有两套独立的日志系统：

| 日志系统 | 用途 | 输出位置 |
|---|---|---|
| `MatrixLogService` | 应用启动、导航等通用日志 | 未统一 |
| `CleanerDiagnosticsLogger` | 清理子系统的诊断日志 | 未统一 |

两者格式不同、路径不同、调用方式不同。

### 6.2 需要达到的效果

1. 整个项目使用统一的日志接口 `ILogger<T>`（`Microsoft.Extensions.Logging`）。
2. 日志输出目标保持为本地文件，日志文件路径和格式与现有保持一致或做到可配置。
3. `MatrixLogService` 的启动日志和 `CleanerDiagnosticsLogger` 的诊断日志统一写入同一个日志文件（或按 Category 分文件但使用相同的格式和基础设施）。
4. 各 Service 和 ViewModel 可以通过构造函数注入 `ILogger<T>` 获得日志能力。
5. 不影响现有日志内容的信息完整度。

### 6.3 验收标准

1. 启动应用后，日志文件包含引擎点火日志和清理诊断日志，格式统一。
2. 搜索日志文件时，能通过 Category 字段区分日志来源（App / CleanerScanService / CleanerExecutionService 等）。
3. `dotnet test` 全量通过。

---

## 7. [P3] 仓库垃圾清理

### 7.1 现状

- 根目录存在 `CleanerAssistantPage.xaml.bak`——代码备份文件，不应在版本控制中。
- `bin/` 和 `obj/` 目录中有大量编译产物。
- `AppPackages/` 中有 `1.0.0.0` 版本的旧打包内容，与当前版本 `1.0.5` 不一致。

### 7.2 需要达到的效果

1. `CleanerAssistantPage.xaml.bak` 从仓库中删除，加入 `.gitignore`（如果 `.bak` 扩展名尚未忽略）。
2. `bin/` 和 `obj/` 中的所有编译产物不再被 Git 追踪。执行 `git status` 时工作区干净。
3. `AppPackages/` 中的过时版本目录被清理或更新为当前版本。

### 7.3 验收标准

1. 执行 `git clone` 后，仓库不包含任何 `.bak` 文件。
2. `git status` 在干净工作区时无输出。
3. `dotnet build` 后，`git status` 无变化（编译产物正确被 `.gitignore` 忽略）。

---

## 8. [P3] 为 Cleaner 子系统撰写架构文档

### 8.1 现状

`docs/` 目录仅有 4 张截图。Cleaner 子系统有 20+ 个 Service，其调用链路、数据流转没有文档，只能通过阅读 `ConfigureServices()` 中的注册顺序和每个 Service 的构造函数参数来反推。

### 8.2 需要达到的效果

在 `docs/` 目录下新增 `cleaner-architecture.md`，内容包含：

1. Cleaner 子系统的整体架构图（Mermaid 流程图），明确标注各 Service 的调用关系和职责边界：
   - 规则加载 → 扫描 → 风险评估 → 边界校验 → 锁定 → 提权判断 → 执行 → 审计
2. 核心概念的简要解释：Safe / Review / ViewOnly 分桶逻辑、隔离区（Quarantine）机制、增量扫描复用窗口
3. 关键文件索引表：列出每个 Service 文件及其对应的测试文件

### 8.3 验收标准

1. `docs/cleaner-architecture.md` 文件存在于仓库中。
2. 文档中的 Mermaid 流程图可在 GitHub 上正确渲染。
3. 新加入项目的开发者阅读该文档后，能理解 Cleaner 的完整数据流，无需询问现有开发者。

---

## 9. [P3] Media 模块测试补齐

### 9.1 现状

| 服务 | 源文件行数 | 测试覆盖情况 |
|---|---|---|
| `MediaScanService` | 302 | 覆盖 `HammingDistance`、`ComputeSHA256Async`、`ComputeDHashAsync`、`ComputeQuickHeaderFooterHashAsync` 等工具方法 |
| `MediaDeduplicationService` | 226 | 零覆盖 |
| `MediaRenameService` | 94 | 零覆盖 |
| `MediaTagService` | 219 | 零覆盖 |
| `ImageProcessingService` | 628 | 测试极少 |

### 9.2 需要达到的效果

为 Media 模块的核心业务逻辑添加测试覆盖：

| 服务 | 需覆盖的关键场景 |
|---|---|
| `MediaDeduplicationService` | 精确匹配（同 MD5）识别为重复；相似匹配（汉明距离阈值内）识别为重复；不同图片不误判；空目录或空文件列表的边界处理 |
| `MediaRenameService` | 基于 EXIF 时间的重命名预览正确性；无 EXIF 时回退到文件修改时间；重名冲突处理策略（自动序号） |
| `MediaTagService` | 标签增删查改；标签筛选过滤；空标签集边界处理 |

### 9.3 验收标准

1. `MediaDeduplicationService` 至少有 4 个测试方法。
2. `MediaRenameService` 至少有 3 个测试方法。
3. `MediaTagService` 至少有 4 个测试方法。
4. 所有测试使用 `TestData/MediaRealWorld/` 中的测试样本文件。
5. `dotnet test` 全量通过。

---

## 优先级总览

| 编号 | 事项 | 优先级 | 预估工时 | 影响范围 |
|---|---|---|---|---|
| 1 | 消除非事件处理器中的 async void | 🔴 P0 | 0.5 天 | 稳定性 |
| 2 | 拆分 CleanerAssistantViewModel | 🟠 P1 | 2 天 | 可维护性 |
| 3 | 补齐 ViewModel 层测试 | 🟠 P1 | 1.5 天 | 质量保障 |
| 4 | API Key 安全存储 | 🟠 P1 | 0.5 天 | 安全性 |
| 5 | 引入 IHttpClientFactory | 🟡 P2 | 0.5 天 | 架构规范 |
| 6 | 统一日志抽象 | 🟡 P2 | 1 天 | 可观测性 |
| 7 | 仓库垃圾清理 | 🟢 P3 | 0.5 天 | 工程卫生 |
| 8 | Cleaner 架构文档 | 🟢 P3 | 1 天 | 团队协作 |
| 9 | Media 模块测试补齐 | 🟢 P3 | 1.5 天 | 质量均衡 |

---

> **审核说明**：请审核人逐项确认目标效果是否合理、验收标准是否可测。任何调整意见请直接在对应条目下批注。
