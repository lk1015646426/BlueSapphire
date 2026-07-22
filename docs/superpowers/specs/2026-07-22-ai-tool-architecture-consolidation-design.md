# BlueSapphire AI 工具架构收口设计

日期：2026-07-22

阶段：第三阶段

状态：待用户书面审阅

## 1. 背景与当前证据

第一阶段已完成开发日志历史和仓库保护；第二阶段已完成 Cleaner 安全闭包。当前 AI 重构仍处于新旧架构并存状态：

- AI 聚焦测试当前为 37/37 通过。
- `AIToolsRegistry.cs` 仍约 1,637 行，但工作树已经删除其中约 572 行领域动作并新增 Provider 架构。
- `IAIToolActionProvider`、`IAIToolCapabilityProvider`、能力模型、能力目录和动作处理注册表尚未被 Git 跟踪。
- Cleaner/Media Action Provider 及专项测试尚未被 Git 跟踪。
- 独立 `AITaskCenterPage`、`AITaskCenterTool` 和 `AICopilotTool` 已在工作树删除，当前源码搜索没有残留引用。
- AI 对话、任务中心和最近活动已在工作树向统一工作台合并，但相关页面差异同时包含视觉调整，必须避免把全局 UI/主题重构混入本阶段。
- 完整工作树全量测试基线为 217/217；已提交 Cleaner 独立快照基线为 206/206。

## 2. 目标

1. 建立模型可见能力与应用动作执行之间的明确边界。
2. 让 `AIToolCapabilityCatalog` 成为模型工具能力的唯一权威目录。
3. 让 `AIToolActionHandlerRegistry` 成为动作名称到领域处理器的唯一分发入口。
4. 将 Cleaner 和 Media 领域动作移出 `AIToolsRegistry`，由独立 Provider 负责。
5. 将 `AIToolsRegistry` 收敛为 Agent 编排、通用动作、隐私安全消息和工具调用协调层。
6. 将 AI 对话、任务中心和最近活动统一到 AI 工作台，删除旧独立入口及残留引用。
7. 保持 Cleaner 和 Media 已验证的安全边界，AI 不得扩大权限或删除范围。
8. 使用临时隔离工作树证明每个候选 AI 提交不依赖当前其他 UI/主题修改。
9. 完成 AI 聚焦测试、全量测试、零警告构建和真实 AI 工作台生命周期验证。
10. 保持 `Assets/DevMatrixLog.json` 哈希不变。

## 3. 非目标

本阶段不执行：

- 不重做全局主题、导航外观或其他页面视觉。
- 不修改 Cleaner 扫描、风险、删除、隔离或恢复策略。
- 不修改媒体识别、重复检测、归档或删除算法。
- 不新增远程 AI Provider、模型供应商或联网协议。
- 不改变 API Key 存储方案。
- 不拆分与本阶段无关的设置页和媒体页面。
- 不发布安装包或更新版本号。
- 不将模型输出作为路径边界、风险等级或用户确认的权威来源。

## 4. 架构职责

### 4.1 `AIToolCapabilityCatalog`

能力目录负责：

- 接收 `IAIToolCapabilityProvider` 提供的能力定义；
- 建立稳定、只读的能力快照；
- 保存工具 ID、动作名、所有者、风险等级和模型函数结构；
- 根据 Provider 工具 ID 补全未显式声明的归属；
- 向模型构造 `ChatTool` 列表；
- 返回克隆，防止调用方修改目录内部状态。

能力目录不执行真实动作，不持有 Cleaner 或 Media 服务。

### 4.2 `AIToolActionHandlerRegistry`

动作注册表负责：

- 使用动作名注册 `AIToolActionHandler`；
- 将参数、确认回调和执行上下文传递给处理器；
- 未知动作返回 `null`，不使用任意反射或字符串执行；
- 重复注册使用明确的“后注册覆盖”语义；
- 只负责分发，不决定风险或权限。

### 4.3 `IAIToolCapabilityProvider`

能力 Provider 描述“模型可以看到什么”，不执行动作。

当前 Provider：

- `CleanerAssistantTool`：Cleaner 能力定义；
- `MediaManagerTool`：Media 能力定义。

工具定义必须与实际动作处理器名称一致。能力目录与动作注册表不允许形成一边存在、一边缺失的幽灵工具。

### 4.4 `IAIToolActionProvider`

动作 Provider 描述“应用如何执行动作”。

当前 Provider：

- `CleanerAIToolActionProvider`；
- `MediaAIToolActionProvider`。

Provider 只能调用所属领域的公开服务和安全入口，不能复制通用 Agent Loop、隐私脱敏或任务中心逻辑。

### 4.5 `AIToolsRegistry`

`AIToolsRegistry` 保留：

- 系统提示构造；
- Agent Loop；
- 模型工具调用解析；
- 隐私安全消息构造；
- 通用应用动作，例如导航、记忆、扩展配置、诊断和受控 HTTP；
- 调用能力目录获取模型工具；
- 调用动作注册表执行领域动作。

`AIToolsRegistry` 不再直接包含 Cleaner 扫描/清理和 Media 分析/整理的大段实现。

### 4.6 `AITaskCenterService`

任务中心负责所有长时间或有副作用的 AI 操作：

- 任务类型与幂等键；
- 单调进度；
- 时间线和摘要；
- 用户取消；
- 页面切换后的持续运行；
- 应用重启后将未结束任务标记为“已中断”；
- 写入、移动和删除任务不自动续跑。

任务中心不得持久化 Cleaner 目标文件清单或敏感原始参数。

## 5. 领域安全边界

### 5.1 Cleaner Provider

- 复用已提交的 `CleanerOperationCoordinator`。
- 复用 Cleaner 扫描范围、风险、执行方式和确认。
- AI 不能把 `ViewOnly` 或 `Permanent` 对象转为自动保洁候选。
- 扫描结果超过有效期后不能用于清理。
- 日志摘要不得返回本地完整路径。

### 5.2 Media Provider

- 媒体分析和整理计划默认只读。
- 完全重复文件必须由 SHA-256 证据支持。
- 相似图片不能自动删除。
- 删除只能进入系统回收站。
- 归档不能覆盖同名文件。
- 未经单独确认，分析授权不能继承为移动或删除授权。

### 5.3 隐私和网络

- 用户名、邮箱、Bearer Token、API Key 和 URL 密钥参数继续在发送远程模型前脱敏。
- 未知工具动作不执行。
- HTTP 动作保持重定向、地址和协议限制。
- 工具参数错误必须返回结构化失败，不得使 Agent Loop 崩溃。

## 6. 收口切片

### 切片 A：能力契约和动作注册

候选文件：

- `Interfaces/IAIToolActionProvider.cs`
- `Interfaces/IAIToolCapabilityProvider.cs`
- `Models/AIToolCapabilityModels.cs`
- `Services/AIToolActionHandlerRegistry.cs`
- `Services/AIToolCapabilityCatalog.cs`
- 对应专项测试。

该切片不依赖页面，不执行领域动作。

### 切片 B：领域 Provider 与 Registry 编排

候选文件：

- `Services/CleanerAIToolActionProvider.cs`
- `Services/MediaAIToolActionProvider.cs`
- `Tools/CleanerAssistantTool.cs`
- `Tools/MediaManagerTool.cs`
- `Services/AIToolsRegistry.cs`
- `Services/AISharedContextService.cs`
- `Services/AIClassifierService.cs`
- `Services/AIOfflineIntentService.cs`
- 对应 Provider 和 Registry 测试。

如果 `AIToolsRegistry` 的通用动作与 Provider 拆分无法独立构建，应按编译证据与切片 A 合并，不允许重新复制领域实现。

### 切片 C：任务中心和 App 注册

候选文件：

- `Services/AITaskCenterService.cs`
- `BlueSapphire.Tests/AITaskCenterServiceTests.cs`
- `App.xaml.cs` 的 AI 最小 DI 增量；
- `Models/AppMessages.cs` 中已经失效的旧消息清理。

`App.xaml.cs` 只提交 AI Provider、能力目录和相关单例/接口注册，不提交全局主题或其他页面改动。

### 切片 D：AI 工作台和旧入口删除

候选文件：

- `Views/AICopilotPage.xaml`
- `Views/AICopilotPage.xaml.cs`
- AI 工作台承载所必需的 `HomePage` 最小接线；
- 删除 `Views/AITaskCenterPage.xaml`；
- 删除 `Views/AITaskCenterPage.xaml.cs`；
- 删除 `Tools/AITaskCenterTool.cs`；
- 删除 `Tools/AICopilotTool.cs`；
- `Tools/HomeTool.cs` 的工作台入口调整。

当前 Home 和 AI 页面包含视觉改动。提交前必须通过从 `HEAD` 生成最小候选文件或经过隔离构建证明这些差异属于 AI 工作台闭包；不得顺带提交全局主题重构。

## 7. AI 工作台行为

统一工作台至少展示：

- AI 对话；
- 当前模型状态；
- 任务中心入口或嵌入任务列表；
- 运行中、已完成、失败、取消和中断状态；
- 最近活动；
- 取消入口；
- 空状态和错误状态。

删除独立任务中心页面后：

- 导航不能再指向旧页面；
- Messenger 消息不能引用已删除类型；
- 旧工具 ID 不能继续出现在能力目录；
- 页面切换不能取消任务；
- 任务中心的持久化和取消能力不能退化为只读展示。

## 8. 依赖闭包验证

每个切片提交前：

1. 暂存明确文件，不使用 `git add .`。
2. 生成 staged patch。
3. 从当前 `HEAD` 创建短路径临时工作树。
4. 只应用候选 patch。
5. 运行 restore、AI 聚焦测试和无增量构建。
6. 编译失败只根据具体类型或资源错误扩大闭包。
7. 成功后删除临时工作树并提交。

混合文件策略：

- `App.xaml.cs`：提交 AI DI 的最小候选内容，主工作树中的主题和其他修改继续保留。
- `HomePage.xaml`、`HomePage.xaml.cs`：只提交 AI 工作台宿主接线；如果与视觉重构无法分离，隔离构建证据必须说明为何属于同一闭包。
- 主题文件默认不进入第三阶段；如 AI 页面仅缺少少量兼容资源，使用最小资源别名，不提交完整主题重构。

## 9. 验收矩阵

| 动作 | 必须满足 | 自动化证据 | 真实证据 |
|---|---|---|---|
| 构建模型工具列表 | 能力稳定、归属和函数结构正确 | Capability Catalog 测试 | AI 工作台加载 |
| 执行已知动作 | 分发到正确 Provider | Handler Registry/Provider 测试 | 只读动作冒烟 |
| 执行未知动作 | 返回未识别，不执行任意代码 | Registry 测试 | AI 错误提示 |
| Cleaner AI 扫描 | 使用共享操作门禁 | Cleaner Provider 测试 | 工作台入口 |
| Cleaner 日志摘要 | 不暴露本地完整路径 | Cleaner Provider 测试 | 对话结果抽查 |
| Media 动作 | 只暴露 Media 动作且保持删除边界 | Media Provider 测试 | 只读分析冒烟 |
| 长任务创建 | 幂等、进度单调、可取消 | Task Center 测试 | 工作台任务列表 |
| 应用重启恢复 | 未完成任务标记为中断 | Task Center 测试 | 重启状态 |
| 页面切换 | 任务继续运行 | Service/ViewModel 测试 | 导航冒烟 |
| 删除旧入口 | 无类型、导航或消息残留引用 | 源码搜索 + 构建 | 导航检查 |
| 关闭窗口 | 后台任务不阻塞 UI 线程 | 生命周期测试 | 正常关闭 |

## 10. 失败与回滚

- 如果 Provider 与能力目录动作名不一致，先修复契约或测试，不在 Registry 中加入模糊别名兜底。
- 如果删除旧页面导致导航失败，恢复最小导航接线，不恢复双页面架构。
- 如果 UI 闭包需要全局主题重构，先尝试最小兼容资源；仍无法分离时停止并重新确认第四阶段边界。
- 如果三个连续修复暴露不同的共享状态所有权问题，停止叠加补丁并重新讨论任务/上下文所有权。
- 所有真实写入、移动、删除测试使用受控临时目录，不操作用户文件。
- 回滚只针对当前 AI 切片，不覆盖 Cleaner、媒体或用户 UI 修改。

## 11. 完成标准

第三阶段只有同时满足以下条件才算完成：

1. Provider 接口、能力模型、能力目录和动作注册表全部被 Git 跟踪。
2. Cleaner/Media Provider 被跟踪并通过专项测试。
3. `AIToolsRegistry` 不再直接实现 Cleaner/Media 领域动作。
4. App 使用最小、明确的 Provider 和目录 DI 注册。
5. AI 工作台保留对话、任务中心、取消和最近活动能力。
6. `AITaskCenterPage`、`AITaskCenterTool`、`AICopilotTool` 删除后无残留引用。
7. AI 聚焦测试不少于当前 37 项且零失败。
8. 完整工作树全量测试不少于当前 217 项且零失败。
9. 已提交 AI 快照在隔离工作树完成全量测试和 0 警告、0 错误构建。
10. 真实 x64 AI 工作台获得非零窗口句柄、`Responding=True`，正常关闭成功。
11. AI 架构文档与实际 Provider、任务和安全边界一致。
12. `Assets/DevMatrixLog.json` 哈希保持不变。
13. 全局主题、设置页和非 AI 页面视觉重构未混入 AI 提交。
