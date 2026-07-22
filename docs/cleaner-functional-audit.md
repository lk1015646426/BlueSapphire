# BlueSapphire 清理助手功能底账

更新时间：2026-07-22

## 1. 产品目标

清理助手不是以“删除数量最多”为目标，而是同时满足四件事：

1. 覆盖足够完整：已知缓存、系统缓存、应用缓存、开发工具缓存、疑似残留和空间大户分别扫描。
2. 结论可以解释：每个结果说明来源、用途、判断依据、删除影响和再生成方式。
3. 动作与风险匹配：已知可再生缓存、需确认对象、用户文件和系统专用清理走不同执行链路。
4. 结果真实可核对：区分“实际释放空间”“移入可恢复暂存”“仅提供查看线索”，不把移动文件包装成释放空间。

AI 负责解释、归纳证据和辅助生成清理计划，不负责绕过规则、边界、权限或用户确认。

## 2. 当前真实运行基线

2026-07-19 的本机快速扫描记录：

- 耗时：31.368 秒。
- 命中：12 个扫描对象。
- 总候选：5,809,046,963 字节。
- 安全清理：2,690 字节，只有 BlueSapphire 日志被归入低风险。
- 建议确认：5,809,044,273 字节。
- 清理执行：0 次。
- 扫描记录中的磁盘范围被写成应用构建输出目录，而偏好设置实际选择的是 `C:\`。

这组数据确认了两个核心故障：

1. 风险评估没有把规则声明的 `RiskLevel` 当作基线，近期仍在使用的已知缓存普遍被降成中风险，造成“扫出很多、默认几乎不清”。
2. 驱动器根路径曾被从 `C:\` 截断为 `C:`，再按当前工作目录解析，深度空间分析可能只分析应用目录。

## 3. 功能层职责

| 层级 | 正确职责 | 当前状态 | 结论 |
|---|---|---|---|
| 快速扫描 | 高频、已知、可再生的当前用户缓存 | 12 条规则，路径固定 | 保留并提升覆盖准确性 |
| 扩展应用扫描 | 更多应用、多配置、多版本缓存 | 31 条深扫规则中的大部分 | 需要安装探测、多配置和进程感知 |
| 系统清理 | Windows Temp、更新、DO、WER、转储 | 7 条提权规则，按目录直接移动 | 需要改为系统专用执行器 |
| 空间分析 | 找出大目录和大文件 | 抽样且最多返回 6 个目录、8 个文件 | 应独立展示，不算可清理空间 |
| 残留分析 | 识别疑似卸载残留 | 启发式、只读 | 保留为调查线索，不自动删除 |
| 风险判断 | 规则基线 + 运行时证据 + 硬边界 | 规则风险基线曾被忽略 | 已开始修复 |
| 执行 | 永久、隔离、回收站、系统动作 | 41 条隔离、2 条永久、0 条回收站 | 需要拆分实际释放与可恢复暂存 |
| 恢复 | 从隔离区回写 | 已实现批次和单项恢复、手动永久清空 | 保留，后续补自动保留期 |
| AI | 解释对象、回答影响、整理计划 | 已有扫描/执行/规则草案工具 | 保留，输入必须使用结构化证据 |

## 4. 内置规则核对（初始 43 条，完成后 45 条）

状态说明：

- **保留**：目标和定位正确，可继续使用。
- **补覆盖**：思路正确，但路径或用户配置覆盖不足。
- **改策略**：扫描对象正确，风险、默认选择或执行方式需要改变。
- **专用执行**：不能继续按普通目录移动/删除处理。

| ID | 当前目标 | 核对结论 | 后续处理 |
|---|---|---|---|
| `bs_temp` | BlueSapphire Temp | 自有可再生产物，当前永久删除 | 保留；按任务状态跳过仍在写入的文件 |
| `bs_logs` | BlueSapphire Logs | 会包含当前正在写入的 `app.log` | 改策略；只处理轮转日志和超过保留期的日志 |
| `user_temp` / `user_temp_review` | `%TEMP%` 分龄内容 | 已拆为 1～7 天确认、7 天以上低风险，1 天内跳过 | 已完成；精确到文件目标并做占用预检 |
| `crash_dumps` | 当前用户 CrashDumps | 会丢失诊断现场 | 已改为 7 天以上、中风险、默认不选择 |
| `thumb_cache` | 缩略图和图标数据库 | 路径正确，可能被 Explorer 占用 | 保留；增加 Explorer 占用说明和专用重建提示 |
| `d3d_cache` | DirectX Shader Cache | 可再生，当前规则为中风险 | 继续验证；优先接入系统清理方式 |
| `edge_http_cache` | Edge 多配置缓存 | 已使用受控通配覆盖 `Default`、`Profile 1/2` 等 | 已补覆盖；后续识别运行进程 |
| `chrome_http_cache` | Chrome 多配置缓存 | 已使用受控通配枚举本地配置 | 已补覆盖；后续识别运行进程 |
| `browser_code_cache` | Edge/Chrome/Brave 多配置 Code Cache | 已覆盖各本地配置，仍将三种浏览器合并展示 | 后续拆分；按浏览器和配置展示来源 |
| `brave_http_cache` | Brave 多配置 HTTP Cache | 已覆盖各本地配置 | 已补覆盖；后续与 Code Cache 按配置归并展示 |
| `firefox_profile_cache` | Firefox `Profiles/*/cache2` | 多配置覆盖合理 | 保留；补充配置名称和浏览器运行状态 |
| `vscode_workspace_cache` | VS Code Cache/CachedData 等 | 基本正确，Service Worker 离线数据影响更大 | 拆分普通缓存与离线缓存，分别解释影响 |
| `discord_cache` | Discord Electron 缓存 | 路径依赖版本，运行时容易占用 | 补覆盖；安装与进程感知 |
| `slack_cache` | Slack Electron 缓存 | 同上 | 补覆盖；安装与进程感知 |
| `teams_classic_cache` | 经典 Teams 缓存 | 只适用于旧客户端 | 保留兼容规则；检测安装后再展示 |
| `zoom_webview_cache` | Zoom WebView2 缓存 | 路径较窄且版本相关 | 补覆盖；检测实际 WebView2 配置目录 |
| `adobe_media_cache` | Adobe Media Cache | 默认路径正确，但 Adobe 支持自定义缓存位置 | 补覆盖；读取 Adobe 配置或只说明“默认位置” |
| `jetbrains_cache` | JetBrains `*/caches` | 会导致索引重建，路径覆盖常用版本 | 保留中风险；展示具体 IDE 和版本 |
| `npm_yarn_cache` | npm `_cacache` + Yarn v6 | 与快速扫描 `npm_cache` 父目录重叠，会重复统计和竞争执行 | 已移除 npm 子路径；后续重命名为 Yarn 规则 |
| `steam_html_cache` | Steam HTML Cache | 路径可能随安装方式变化 | 补覆盖；探测 Steam 安装目录和运行状态 |
| `github_desktop_cache` | GitHub Desktop Electron 缓存 | 基本合理 | 补安装和进程感知 |
| `postman_cache` | Postman Electron 缓存 | Service Worker 数据影响需要单独解释 | 拆分普通缓存与离线数据 |
| `obsidian_cache` | Obsidian Electron 缓存 | 不应触碰 Vault，本规则当前只指向应用缓存 | 保留；明确不会扫描笔记库 |
| `notion_cache` | Notion Electron 缓存 | 可能影响离线内容 | 保留中风险；突出离线内容影响 |
| `figma_cache` | Figma Electron 缓存 | 可能导致资源重新下载 | 保留中风险；补运行状态 |
| `lark_feishu_cache` | 飞书/Lark 多分区缓存 | 路径多但版本依赖明显 | 补安装探测；按实际命中分区合并展示 |
| `dingtalk_cache` | 钉钉 Electron 缓存 | 客户端版本路径差异较大 | 补覆盖；安装和进程感知 |
| `onedrive_logs` | OneDrive 日志 | 不是纯缓存，会损失同步故障诊断记录 | 改策略；归入诊断记录并设置年龄阈值 |
| `epic_launcher_cache` | Epic Web Cache 多版本目录 | 已改为 `webcache*` 安全枚举 | 已补覆盖；后续识别运行状态 |
| `battle_net_cache` / `battle_net_agent_cache` | Battle.net 用户缓存与 ProgramData Agent 缓存 | 已按权限边界拆分 | 已完成；Agent 规则要求管理员模式 |
| `ea_app_cache` | EA Desktop Electron 缓存 | 基本合理 | 补安装和进程感知 |
| `ubisoft_connect_cache` | Ubisoft cache | 安装位置可能不在 LocalAppData | 补覆盖；读取安装位置 |
| `msteams_webview_cache` | 新 Teams Store 包缓存 | 包 SID 固定路径较脆弱 | 补包探测；使用实际安装包目录 |
| `windows_temp` | `%WINDIR%\Temp` | 需要管理员，且应跳过在用和近期文件 | 专用执行；按年龄和占用处理 |
| `windows_update_download` | SoftwareDistribution Download | 组件重置属于故障排查，不应作为日常保洁 | 已改为高风险只读提示，交由 Windows 设置/排障流程 |
| `delivery_optimization_cache` | DO Cache | Windows 提供 `Delete-DeliveryOptimizationCache` | 已改用 DeliveryOptimization cmdlet 专用执行 |
| `windows_wer_reports` | WER Archive/Queue | 会丢失问题诊断和待上传报告 | 改策略；按年龄、队列状态和用户确认处理 |
| `windows_wer_temp` | WER Temp | 可清理但可能仍在采集 | 专用执行；检查 WER 状态和占用 |
| `windows_minidump` | Minidump | 不是普通垃圾，是蓝屏诊断证据 | 改策略；默认保留/确认，支持导出后删除 |
| `live_kernel_reports` | LiveKernelReports | 同样属于内核诊断证据 | 改策略；默认保留/确认，按年龄展示 |
| `pip_cache` | pip cache | 路径正确，可再下载 | 保留；优先使用 `pip cache purge` 或明确永久删除 |
| `nuget_http_cache` | NuGet v3 cache | 路径正确，当前永久删除 | 保留；优先使用 `dotnet nuget locals http-cache --clear` |
| `npm_cache` | 整个 npm-cache | 路径正确但与深扫规则曾重叠 | 保留；优先使用 `npm cache` 能力并核对缓存完整性 |

## 5. P0～P3 完成状态

### P0：基础可信度——完成

- 修复驱动器根路径截断、风险规则基线失效、目录大小重复计算和 npm/Yarn 重叠。
- 批次、AI、UI 与遥测统一区分处理量、真实释放量和可恢复暂存量。
- 增加空间口径版本，旧历史和旧审计首次加载时会自动重算并迁移落盘。
- AI 只能补充解释，不能改变风险、可选择性、执行方式或默认选择。
- 受控真实文件系统已走通扫描、隔离、恢复、再次隔离和永久清空。

### P1：核心安全与系统能力——完成

- 用户 Temp 分为 1～7 天建议确认、7 天以上低风险永久清理；1 天内文件不进入候选。
- 年龄规则生成精确文件目标，不会因目录内存在旧文件而删除整个目录。
- 规则可声明占用预检；命中运行进程或文件资源占用时，在确认前禁止选择。
- 崩溃转储、OneDrive 日志、WER、Minidump、LiveKernelReports 增加年龄条件并默认确认。
- Delivery Optimization 使用微软 `Delete-DeliveryOptimizationCache` 专用命令。
- Windows Update 下载目录改为只读占用提示，不把组件重置冒充日常保洁。

### P2：覆盖、版本与运行状态——完成

- Edge、Chrome、Brave、Firefox 覆盖多配置，并在结果中展示配置名称。
- 主要浏览器、Electron 应用、游戏平台、Adobe、JetBrains 和同步工具增加运行进程识别。
- 从 Windows 卸载注册表补充已安装应用名称、版本和安装位置。
- Steam、Ubisoft 增加注册表解析的非默认安装缓存路径。
- Battle.net 用户缓存与 ProgramData Agent 缓存拆分，后者明确要求管理员模式。
- Epic `webcache*` 和浏览器通配规则覆盖版本变化，同时保持规则边界限制。

### P3：产品闭环与 UI——完成

- 主流程固定为“选择范围 → 扫描 → 查看证据 → 核对计划 → 执行 → 恢复或释放”。
- 低风险、建议确认、Windows 专用处理和仅供查看四类对象分开展示。
- 详情展示来源应用/配置/版本、年龄条件、占用状态、风险依据、删除影响和 AI 辅助解释。
- 清理计划分别展示永久释放、可恢复暂存和 Windows 系统动作。
- 隔离区支持单项/批次恢复、手动永久清空和可选的 3/7/14/30 天自动保留期。
- 实际释放量按磁盘根目录分别统计，避免把跨盘动作混成一个模糊数字。

## 6. 执行与空间口径

批次结果使用三个不同数字：

- `ProcessedBytes`：成功完成动作的对象体积。
- `ReleasedBytes`：永久删除或系统专用清理后实际释放的体积。
- `RecoverableBytes`：进入隔离区或回收站、仍占用某块磁盘的体积。
- `ReleasedBytesByDrive`：按 `C:\`、`D:\` 等磁盘根目录记录释放量。

旧数据通过 `AccountingVersion = 2` 自动迁移。UI、AI 和遥测不得把 `RecoverableBytes` 写成“已释放”。

## 7. AI 证据契约

AI 对每个清理对象只能基于以下结构化证据解释：

- 规则 ID、规则版本和规则声明风险。
- 实际命中路径和来源应用。
- 文件数、实际统计体积、最早/最新修改时间。
- 是否命中用户数据扩展名、同步目录、系统边界、重解析点或占用进程。
- 执行方式、是否立即释放、是否可恢复。
- 规则说明、删除影响和再生成方式。
- 判断来源：人工规则、系统能力、启发式分析或 AI 补充说明。

AI 可以建议“清理/确认/保留/调查”，但最终可选择性由领域策略决定。

## 8. 真实工作流复审与完成口径

P0～P3 表示功能范围已经实现，不再等同于“真实用户流程已经验收”。2026-07-19 重新按选择磁盘、重复扫描、取消、清理、失败重试、排除、恢复、隔离区清空、自动保洁和关闭窗口逐条复审，发现并修复了以下跨模块状态问题：

- 扫描、清理、失败重试、恢复和隔离区清空统一使用共享忙碌门禁，UI 不再显示可点但命令静默拒绝，也不再允许两个磁盘修改任务并发。
- 清理、失败重试和自动保洁必须先释放当前忙碌状态，再启动结果刷新；自动保洁完成后不再保留旧扫描结果。
- 自动保洁即使没有发现安全项，也会记录本轮已处理，避免每次启动重复触发。
- 新扫描立即清空旧详情；加入排除立即取消当前选择，排除列表变化会排队触发一致性扫描。
- 恢复操作的忙碌范围覆盖文件恢复、历史刷新和审计落盘，不在半更新状态下提前放开扫描。
- 规则在扫描或清理期间变化时不再丢弃刷新请求，会在共享操作结束后补做一次快速扫描。
- 自动化和遥测设置保存后，把服务返回的真实状态同步回界面，而不是继续显示旧计划或旧上传状态。

此后“完成”必须同时满足 [清理助手真实流程验收矩阵](cleaner-workflow-acceptance.md)，不能再只依据静态代码检查、单次构建或功能清单下结论。后续新增应用规则、适配新版本路径或增加 Windows 官方清理能力仍属于持续规则维护；所有新规则继续遵守：来源可解释、边界可验证、执行前可核对、AI 不得越权。

## 9. 官方系统能力参考

- Microsoft Storage Sense：<https://learn.microsoft.com/windows/configuration/storage/storage-sense>
- Delivery Optimization 缓存清理：<https://learn.microsoft.com/powershell/module/deliveryoptimization/delete-deliveryoptimizationcache>
- Delivery Optimization 测试与清理示例：<https://learn.microsoft.com/windows/deployment/do/delivery-optimization-test>
## 10. 2026-07-22 Cleaner 安全闭包收口

本轮没有扩大扫描或删除范围，而是把现有安全重构整理为可以脱离脏工作树独立构建的提交闭包。

### 已提交闭包

1. 扫描与执行安全闭包：规则、模型、路径边界、驱动器范围、风险基线、扫描、状态迁移、执行、恢复、系统动作和空间统计。
2. 工作流与安全界面闭包：共享操作协调器、扫描/清理/恢复/重试/自动保洁状态、命令可用性、Cleaner 页面、确认对话框和圆环统计。
3. 混合文件采用最小提交：`App.xaml.cs` 只提交 3 个 Cleaner DI 注册；`Themes/SharedTheme.xaml` 只提交 Cleaner XAML 所需的 3 个兼容资源，其他 AI 和全局主题重构仍保留在工作树。

### 依赖闭包证据

- 扫描规则切片单独构建时，编译器证明 `CleanerStateStore` 直接依赖执行层的 `CurrentAccountingVersion`，因此扫描和执行切片按证据合并。
- 协调器切片单独构建时，编译器证明页面必须实现新的清理计划、扫描提醒和隔离区清空确认接口。
- 加入页面后，编译器证明新版页面统计直接依赖新版 `DonutChart` 签名，因此圆环控件进入同一 UI 闭包。
- 合并后的两个候选闭包都在临时工作树完成 restore、聚焦测试和无增量构建，均为 0 警告、0 错误。

### 当前验证

- Cleaner 聚焦测试：102/102 通过。
- 完整工作树全量测试：217/217 通过。
- 已提交 Cleaner 独立快照全量测试：206/206 通过。
- 真实 x64 Release Cleaner 窗口可响应并正常关闭，句柄 `2363504`，退出码 0；最终 Debug 验证实例句柄 `3673690`、`Responding=True`，保持运行。
- 开发日志源码哈希保持不变。

### 延后范围

`CleanerAIToolActionProvider` 在完整工作树中通过专项测试，但依赖通用 AI action 接口、处理器注册表和 App 注册，按产品边界延后到 AI 架构收口阶段，不复制 Cleaner 私有版通用接口。

### 最终代码审查修复

代码审查发现 CleanerOperationCoordinator.StateChanged 订阅者异常会从 TryAcquire 逸出，使已获得的租约无法交还调用方并可能长期保持忙碌。新增 RED 测试 ThrowingStateChangedSubscriber_DoesNotLeakOperationLease 后，将状态通知改为逐订阅者隔离；聚焦测试 3/3、最近工作流测试 24/24、隔离工作树测试和构建均通过。
