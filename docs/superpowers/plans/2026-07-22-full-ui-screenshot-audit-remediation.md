# BlueSapphire 全界面截图审计与 UI 修复实施说明

状态：实施中（2026-07-31 第二窗口：DevLog/Home/AI/Cleaner 第二轮页面级修复已写入并通过回归；Debug/Release 构建与截图验收待续）

基线提交：`b71ab9f`

审计日期：2026-07-22

## 第二开发窗口进度（2026-07-31 晚）

本窗口按“暂停时尚未完成的工作”清单继续，先补页面级 P1，再统一回归验证。

### 本窗口已完成的实现

- `Views/DevLogPage.xaml`（计划第 6 节）
  - 日志卡片增加 `CardNarrow`/`CardWide` 布局状态（模板内 AdaptiveTrigger，阈值 560，按卡片实际宽度响应）；
  - 窄卡片把版本/时间（`CardMetaPanel`）移到标题下方第二行，动作按钮（`CardActionsPanel`）改为左对齐，避免右侧信息越界；
  - 详情覆盖层改为真正居中的有限宽高面板（`DetailPanel`，MaxWidth 760、MaxHeight 640，水平/垂直 Center），整体面板化（PanelSurface + 边框 + 圆角），关闭按钮固定在面板右上角，保留 OverlayScrim 压低底层对比度。
- `Views/AICopilotPage.xaml`、`HomePage.xaml`（计划第 2 节）
  - 消息外层容器移除 `HorizontalAlignment={Binding Alignment}` 与固定 `MaxWidth=760`，改为整行 Stretch；
  - 内层气泡 `MessageBubble` 负责左/右对齐并以 `MaxWidth=640` 约束最大宽度，气泡宽度随父容器收缩；
  - 工具消息由横向 StackPanel 改为 `ToolMessageGrid`（Auto/Auto/*），长文本在 `*` 列内换行，不再把气泡顶出视口；
  - `HomePage` 增加高度状态 `ShortHomeLayout`/`TallHomeLayout`（MinWindowHeight 780）：小高度窗口 AI/任务区按 0.9*/1.1* 分配，高窗口按 1.15*/0.85* 分配。
- `CleanerAssistantPage.xaml`、`CleanerAssistantPage.xaml.cs`（计划第 4 节第二轮）
  - 布局断点由窗口宽度 AdaptiveTrigger 改为 `CleanerLayoutRoot_SizeChanged` → `ApplyCleanerLayout` 按**页面实际内容宽度**驱动（<720 Narrow、<1080 Medium、否则 Wide）；
  - 新增 `MediumLayout`：内部导航栏收缩为 56px 图标栏（`TabLabelCleanup/Automation/History` 隐藏，MainTabBar 项改为图标+文字并补 AutomationProperties.Name），扫描详情栏 MinWidth 降为 200；
  - 窄布局新增 `CompactSectionPicker`（顶部 ComboBox）替代被收起的内部导航，与 MainTabBar 双向同步，窄屏不再丢失分区切换入口；
  - 磁盘行模板增加 `DriveRowCompact`/`DriveRowWide` 状态：行宽不足时隐藏占用条（`DriveUsageBar`）并归零其列宽；
  - 未修改 Cleaner 扫描、清理、风险判断或业务算法，未在 code-behind 引入与 ViewModel 并行的业务状态机。

### 本窗口新增契约测试

- `UiPageContractTests.CleanerWorkspace_UsesContentWidthDrivenMediumLayoutAndCompactSectionPicker`
- `UiPageContractTests.DevLogList_AdaptsCardMetaAndCentersDetailOverlay`
- `UiPageContractTests.AiCopilot_MessagesStretchAndBubbleWrapsWithinParentWidth`

### 本窗口验证记录

- 修改文件均通过 XML well-formed 检查。
- 全量测试（管理员会话）：239 通过 / 240 总计，唯一失败为 `CleanerAssistantViewModelTests.IsElevatedMode_ReflectsActualPrivilegeState`——该用例断言“当前进程未提权”，而本会话为高完整性级别（S-1-16-12288），属环境敏感断言，非本轮回归；排除该用例后 239/239 通过。**下一窗口需在普通（非管理员）会话重跑全量确认 0 失败。**
- 测试总项数由 236 增至 240（本窗口净增 3 项契约 + 第一窗口契约入库）。
- `Assets/DevMatrixLog.json` 测试前后 SHA-256 均为 `A9460CC2C03CFED65B36BE74E91D5CD2FBACD978D4243F8E6EFB9C81EBB64FEC`。
- 沙箱会话对 `obj\x64\Debug` 下历史中间文件（ACL 含 CodexSandboxUsers）无写权限，导致 `dotnet build --no-incremental` 出现 APPX0002 假死/拒绝访问；改用非沙箱会话构建（Restart Manager 确认无进程占用该文件）。

### 第二窗口暂停时未完成

- ~~Debug/Release `--no-incremental` 全量构建 0 警告 0 错误~~：
  - **Debug x64 全部重新生成已通过（2026-08-01 00:29，VS 18 Insiders）**：BlueSapphire + BlueSapphire.Tests 2 项目全部成功、0 失败、无编译警告输出（仅 NETSDK1057 预览 SDK 信息性消息），耗时 50.1s；应用随后在调试器中正常启动，验证本轮 XAML 可编译可加载。本 shell 会话中 `dotnet build` 对 slnx 连续三次死锁（进程 0% CPU、无文件写入），属 CLI 环境怪癖，改用 VS MSBuild 完成；
  - Release x64 全部重新生成：待执行。
- Media、Cleaner、DevLog、Home/AI、Settings 的 1600×900、1280×800、900×700 真实截图验收；
- 标准/大字号、Light/Dark、非默认主题、减少动态效果组合抽查；
- 所有 ContentDialog 真实按钮可见性截图（Task 5）；
- 普通（非管理员）会话重跑全量测试。

## 第一开发窗口进度（截至 2026-07-31 白天）

本窗口已按“先对话框、再页面根布局、最后逐页验收”的顺序完成一轮修复，当前按用户要求暂停。以下内容已经写入工作区，但尚未宣称全 UI 修复完成。

### 已完成的实现

#### Task 1：统一响应式 ContentDialog 契约

已完成以下对话框的主要响应式修复：

- `Views/Dialogs/AdvancedImageEditorDialog.xaml`
  - 移除阻塞常用视口的固定最小宽度和固定参数栏宽度；
  - 增加宽/中/窄布局状态；
  - 内容区滚动，底部动作由 ContentDialog 保持可达。
- `DuplicateResultDialog.xaml`
  - 移除固定宽度；
  - 列表高度收紧并允许滚动；
  - 增加响应式布局状态。
- `Views/Dialogs/EnhanceImageDialog.xaml`
  - 移除固定宽度、高度和预览高度；
  - 参数区宽屏使用 2×2，窄屏切换为单列；
  - 保留“本机处理、不上传”提示。
- `Views/DevLogInputDialog.xaml`
  - 移除固定宽度和旧的固定最大高度；
  - 版本、分级、日期增加三列/两列/单列布局状态；
  - 正文内容区滚动，动作栏保持在 ContentDialog 底部。
- `RenamePreviewDialog.xaml`、`Views/Dialogs/FormatConvertDialog.xaml`
  - 移除主要固定宽度并增加响应式状态，降低动作区贴边和裁切风险。

#### Task 4：主题与对话框按钮基础修复

- `Themes/SharedTheme.xaml`
  - `SubtleActionButtonStyle` 改为弱表面按钮，不再使用透明背景或主题主色作为取消按钮底色；
  - 增加悬停、按下状态，保持取消按钮与主要动作的层级差异。
- `App.xaml.cs`
  - 新增运行时原生强调色资源同步；
  - 覆盖 WinUI 原生控件实际使用的 Accent Fill、Accent Text 和 System Accent 资源，使主题预设能够同步影响 RadioButton、ToggleSwitch、Slider 等控件。

#### Task 2：窗口外壳与页面根宽度闭包（第一轮）

- `MainWindow.xaml`
  - 保持 `ContentFrame` 的水平、垂直 Stretch；
  - 将导航栏打开宽度从 248 调整为 220；
  - 将导航栏紧凑/展开阈值调整为 900/1280，避免在内容区域已经变窄时仍过早展开完整导航栏。

#### Task 3：页面级响应式修复（第一轮）

- `MediaManagerPage.xaml`、`MediaManagerPage.xaml.cs`
  - 新增基于页面实际内容宽度的 `CompactMediaLayout` / `WideMediaLayout`；
  - 内容宽度不足时收起所选图片详情栏，保留所选操作区的水平滚动能力；
  - 选中详情栏的显示逻辑与当前实际布局状态同步，避免窄屏右侧详情栏把主内容挤出视口。
- `SettingsPage.xaml`、`SettingsPage.xaml.cs`
  - 主题预设网格改为根据实际可用宽度动态使用 4/2/1 列；
  - 宽屏默认首屏为 4×2，较窄区域切换为 2 列或单列；
  - 页面根布局改为 Stretch，并在尺寸变化时重新排列主题预设。
- `CleanerAssistantPage.xaml`
  - 对隐藏的历史明细模板收口部分绑定为 `Binding, Mode=OneTime`，移除会触发当前 XAML 编译链路错误的内部模板类型声明；
  - 未修改 Cleaner 扫描、清理、风险判断或业务算法。

### 已增加或更新的契约测试

- `BlueSapphire.Tests/UiDialogContractTests.cs`
  - 覆盖响应式对话框固定宽度移除、滚动约束、AdaptiveTrigger、取消按钮弱表面和原生 Accent 资源同步。
- `BlueSapphire.Tests/UiPageContractTests.cs`
  - 覆盖 DevLog 输入对话框响应式契约；
  - 覆盖媒体页窄屏所选操作可达性及实际内容宽度状态；
  - 覆盖设置页主题预设 4/2/1 响应式布局；
  - 覆盖 MainWindow 导航栏宽度与内容 Frame Stretch 契约。

### 当前验证记录

- 已通过 XML well-formed 检查：本轮修改的对话框、共享主题和 Cleaner 页面均可解析。
- 已通过聚焦对话框/页面契约测试：7/7。
- 在本窗口新增媒体、设置和 MainWindow 修复后，已通过聚焦契约测试：3/3。
- 在新增上述页面级修复之前，已通过全量测试：236/236，0 失败。
- 在新增上述页面级修复之前，Debug 构建和 Release 构建均为 0 警告、0 错误。
- `Assets/DevMatrixLog.json` 当前 SHA-256 保持不变：
  `A9460CC2C03CFED65B36BE74E91D5CD2FBACD978D4243F8E6EFB9C81EBB64FEC`
- 已启动真实 x64 Cleaner 进程进行基础窗口存活检查，进程响应正常；完整截图验收矩阵尚未完成。

### 暂停时尚未完成的工作

- 重新运行页面级修复后的全量测试，以及 Debug/Release 全量构建；
- 完成 Media、Cleaner、DevLog、Home/AI、Settings 的 1600×900、1280×800、900×700 真实截图验收；
- 完成标准字号、大字号、Light/Dark、非默认主题和减少动态效果组合抽查；
- 进一步处理 Cleaner 自动化/规则/记录页的真实截图裁切问题；
- 进一步处理 DevLog 列表和详情覆盖层在窄内容区的动作布局；
- 复核 Home/AI 长消息在真实进程中的换行表现；
- 完成所有 ContentDialog 的真实按钮可见性截图；
- 按 Task 5 更新完整截图集后，才能根据“完成标准”再次标记全 UI 修复完成。

## 审计范围

- 6 个页面：工作台、AI 工作区、媒体工作台、Cleaner、设置、更新日志；
- Cleaner 3 个内部工作区：清理、自动化与规则、记录；
- 更新日志列表与详情覆盖层；
- 7 个 ContentDialog：重命名预览、重复结果、相似候选、格式转换、自动增强、高级编辑、Skill 网络配置、更新日志输入；
- 主要视口：1440×900、1600×900、900×700；
- 主题：Dark/Light，Default/Sky/Ochre/Sepia；
- 标准字号与大字号；减少动态效果开启；
- 系统 DPI：96（100%）。

所有截图均来自真实 x64 WinUI 进程，截图入口仅存在于临时隔离工作树，不修改产品主工作树。

## 总体结论

**视觉语言已经统一，但布局闭包尚未完成。**

优点集中在字体、色彩、卡片、状态语义和导航一致性；主要问题集中在固定宽度、内部双侧栏、ContentDialog 最大宽度和缺少真正的宽度断点。1600×900 下仍存在操作区越界，说明这不是单纯的窄窗口问题。

### 评分

| 维度 | 评分 | 结论 |
|---|---:|---|
| 视觉一致性 | 7/10 | 字体、圆角、面板和语义色较统一 |
| 信息层级 | 7/10 | 标题和主操作总体清晰，空状态文案较好 |
| 响应式布局 | 3/10 | 多个页面和对话框在 1440/1600 宽度仍越界 |
| 操作可达性 | 4/10 | 多个对话框的确认/取消按钮落在视口之外 |
| 主题完整性 | 6/10 | 页面主题生效，但原生选中态仍混入系统蓝色 |
| 可访问性呈现 | 6/10 | 文本和焦点基础较好，但裁切会直接破坏键盘/鼠标可达性 |

## 必须优先修复

### P0：高级图片编辑器无法在常用视口完整操作

证据：`19-dialog-advanced-editor.png`、`31-wide-dialog-advanced-1600.png`。

- 对话框同时横向和纵向越界；
- 右侧参数栏被裁切；
- 底部“应用并导出/取消”不可见；
- `MinWidth=780`、内容双列和 `ContentDialogMaxWidth=1200` 共同造成布局失控。

建议：改为响应式双栏/单栏工作区；视口不足时右侧参数栏改为 320px 抽屉或下方滚动区；底部动作栏固定在可视区域。

### P0：重复/相似结果对话框的动作按钮不可达

证据：`15-dialog-duplicate.png`、`16-dialog-similar.png`、`29-wide-dialog-duplicate-1600.png`。

- 对话框右侧和底部越界；
- 主要动作完全不在截图范围内；
- 列表内容没有为动作栏预留高度。

建议：取消固定 `Width=620`，使用 `MaxWidth` + `HorizontalAlignment=Stretch`；列表设置基于窗口高度的 `MaxHeight`；底部动作栏保持固定。

### P0：自动增强、更新日志输入等长对话框底部动作被裁切

证据：`18-dialog-enhance.png`、`21-dialog-devlog-input.png`、`30-wide-dialog-enhance-1600.png`、`32-wide-dialog-devlog-input-1600.png`。

建议：ContentDialog 内容只负责滚动，标题和底部动作栏不参与滚动；内容高度使用窗口高度减去标题栏和动作栏的动态上限。

## 高优先级布局问题

### P1：应用内容在 1440×900 和 1600×900 下仍发生水平溢出

受影响截图：

- 工作台：`01-home-wide.png`、`02-home-narrow-large.png`、`22-wide-home-1600.png`；
- 媒体：`03-media-empty.png`、`04-media-populated.png`、`23-wide-media-populated-1600.png`；
- Cleaner：`05-cleaner-scan.png`、`06-cleaner-automation.png`、`07-cleaner-history.png`、`24-wide-cleaner-scan-1600.png`、`25-wide-cleaner-automation-1600.png`；
- 更新日志：`12-devlog-list.png`、`13-devlog-detail.png`。

具体表现：

- 工作台 AI 消息不换行，正文在右侧被截断；
- 媒体搜索、筛选、导入和空状态整体向右越界；
- Cleaner 磁盘占用、全选按钮、规则导入/恢复按钮被裁切；
- 更新日志“添加记录”、版本信息、卡片动作和详情关闭入口落在右侧视口外。

建议：以 **内容区域实际宽度** 而非顶层窗口宽度驱动断点；去掉页面级固定 `Width/MinWidth`；复杂工具栏在 900–1200 内容宽度下折行或收进 `CommandBar`/`MenuFlyout`。

### P1：Cleaner 内外双导航占用过高

Cleaner 同时存在应用 NavigationView 和页面内部 156px 导航栏，导致主任务区被压缩。自动化页和记录页顶部又增加横向 Tab 条。

建议：

- 内容宽度不足时将 Cleaner 内部导航改成顶部 ComboBox/横向紧凑 Tab；
- 不要只隐藏右侧详情面板，主导航自身也应缩减；
- 自动化子页的 5 个标签应使用可滚动 Tab 或溢出菜单。

### P1：设置页宽屏仍使用单列主题列表，窄屏扩展卡片却保持双列

证据：`09-settings-top.png`、`26-wide-settings-top-1600.png`、`10-settings-middle.png`、`27-wide-settings-middle-1600.png`。

- 8 套主题单列排列造成大量无效空白和超长滚动；
- “MCP 服务器/在线 API”等扩展卡片继续并排，右侧卡片越界；
- 同一个页面在该收缩的地方不收缩，在该展开的地方没有展开。

建议：主题预设使用响应式 `ItemsWrapGrid`：宽屏 4 列、中等 2 列、窄屏 1 列；扩展卡片在内容宽度不足时改为单列。

### P1：DevLog 列表和详情覆盖层缺少宽度闭包

证据：`12-devlog-list.png`、`13-devlog-detail.png`。

- 列表卡片的版本、日期、动作按钮位于可视区域外；
- 详情覆盖层只显示中部内容，关闭入口不可见；
- 覆盖层与底层内容的层级区分不足。

建议：列表卡片宽度绑定父容器；版本/时间在中宽度下移动到第二行；详情改为真正居中的有限宽度面板，关闭按钮固定在右上角。

## 中优先级视觉问题

### P2：主题预设与原生控件强调色不一致

Ochre 深色主题中，主题色是橙色，但 RadioButton、ToggleSwitch 和取消按钮仍使用亮蓝/青色。证据：`09-settings-top.png`、`10-settings-middle.png`、多个对话框。

建议：运行时同步 WinUI 原生 Accent 资源，避免 `StaticResource` 锁定系统强调色；选中、焦点、Toggle、Slider 和 Dialog 次要按钮全部使用当前主题语义色。

### P2：取消按钮视觉权重过高

重命名、格式转换等对话框中，取消按钮使用高饱和蓝色，与主要动作几乎同权重。

建议：取消使用透明或弱表面按钮；危险主要动作使用危险色，普通主要动作使用品牌主色。

### P2：空状态位置和空间利用不稳定

- 媒体空状态整体偏右且部分越界；
- Cleaner 记录空状态只占页面上部，余下区域过空；
- 工作台 AI 区在无对话时占用大量高度，任务区域被压到下方。

建议：空状态应以当前内容容器居中，而不是按理想设计宽度定位；工作台根据任务数量动态调整 AI/任务区域比例。

## 逐界面结论

| 界面 | 结论 | 主要问题 |
|---|---|---|
| MainWindow/导航 | 基础合理 | 导航稳定，但内容区没有为页面提供可靠宽度约束 |
| 工作台/AI | 需修改 | 消息文本水平裁切，任务区在小高度下过低 |
| 媒体空状态 | 需修改 | 空状态与导入按钮向右越界 |
| 媒体有内容 | 需修改 | 搜索/筛选/导入区越界，详情区未形成稳定布局 |
| Cleaner 清理 | 需修改 | 内外双导航，磁盘信息和顶部动作被裁切 |
| Cleaner 自动化 | 需修改 | 顶部标签和卡片右侧动作越界 |
| Cleaner 记录 | 基本可用 | 顶部最后入口轻微裁切，空状态利用率较低 |
| 设置 | 需修改 | 主题网格和扩展卡片断点方向相反 |
| 更新日志列表 | 需修改 | 标题动作和卡片右侧信息不可见 |
| 更新日志详情 | 需修改 | 关闭入口不可见，覆盖层未居中闭合 |
| 重命名预览 | 需修改 | 底部动作部分越界，取消权重过高 |
| 重复/相似结果 | 阻塞 | 主要动作不可见 |
| 格式转换 | 需修改 | 底部按钮贴边/裁切，取消按钮过强 |
| 自动增强 | 阻塞 | 内容过高，动作栏不可见 |
| 高级编辑 | 阻塞 | 横纵双向越界，无法完成操作 |
| Skill 网络配置 | 基本可用 | 尺寸偏宽、位置偏低，取消按钮孤立 |
| 更新日志输入 | 阻塞 | 右侧和底部越界，保存/取消不可见 |

## 优点

- Segoe UI Variable、圆角、细边框和面板层次已经形成统一工作台语言；
- Dark/Light 的基础对比度和正文可读性总体良好；
- Cleaner 风险色、媒体类型标签、警告条和成功提示具备明确语义；
- 页面导航名称一致，空状态大多提供下一步动作；
- 对话框标题和说明文案能够解释任务、影响和不可逆性。

## 推荐修复顺序

1. 建立统一的 `ResponsiveContentDialog` 尺寸契约，先修所有不可达动作；
2. 修 MainWindow 内容区和页面根容器的实际宽度约束；
3. 修媒体、Cleaner、DevLog 的横向溢出；
4. 重做设置页响应式网格；
5. 同步主题强调色到 WinUI 原生控件；
6. 最后调整空状态、密度和次要视觉层级。

## 截图索引

- 页面总览：`contact-pages.png`
- 设置与更新日志：`contact-settings-devlog.png`
- 对话框：`contact-dialogs.png`
- 1600×900 复核：`contact-wide-1600.png`
- 单张截图：`01-*.png` 至 `33-*.png`


---

## 下一窗口使用说明

本文是后续 UI 修改工作的事实依据。下一窗口开始前依次读取：

1. `.agents/AGENTS.md`；
2. `docs/superpowers/specs/2026-07-22-ui-theme-shell-consolidation-design.md`；
3. `UI_REDESIGN_AUDIT.md`；
4. `docs/superpowers/plans/2026-07-22-full-ui-screenshot-audit-remediation.md`；
5. 修改 Cleaner 时再读取 `docs/cleaner-workflow-acceptance.md`。

不要直接一次修改所有 XAML。先关闭 P0 对话框问题，再修页面级 P1；每个切片均需要失败测试、XAML 编译和真实截图。

## 具体源码定位与修改要求

### 1. MainWindow 内容宽度闭包

文件：`MainWindow.xaml`

关键位置：

- `OpenPaneLength="248"`；
- `CompactModeThresholdWidth="760"`；
- `ExpandedModeThresholdWidth="1040"`；
- `ContentFrame`。

问题：NavigationView 打开后，页面实际内容宽度明显缩小，但页面仍按理想窗口宽度布局。媒体、Cleaner、设置和 DevLog 的右侧越界都被这一点放大。

修改要求：

- ContentFrame 和页面根容器必须 `MinWidth=0`、水平 Stretch；
- 页面断点使用页面根容器的实际宽度，不使用顶层窗口宽度猜测；
- 内容不足时应更早进入 Compact 导航；
- 不能用简单 Clip 掩盖按钮不可达问题。

验收：导航展开和收起后，1600×900、1280×800、900×700 均无水平溢出。

### 2. 工作台和 AI 消息

文件：

- `HomePage.xaml`；
- `Views/AICopilotPage.xaml`。

关键位置：

- Home 两个根行均为 `Height="*"`；
- AICopilot 消息 Grid 使用 `HorizontalAlignment="{Binding Alignment}"`；
- 消息 Grid 固定 `MaxWidth="760"`；
- `ChatScrollViewer`。

问题：消息容器根据内容 DesiredSize 横向扩张，长消息直接被右侧裁切；窄窗口下 AI 区占据过多高度，任务区被压到下方。

修改要求：

- 外层消息容器 Stretch；
- 只在内层气泡控制左/右对齐；
- 气泡最大宽度根据父容器计算；
- 普通文本、Markdown、工具消息都必须 Wrap；
- 小高度窗口调整 AI/任务区域比例；
- 被隐藏的顶栏工具放入“更多”菜单。

验收：长中文、长英文 URL、长路径均不得产生水平越界。

### 3. 媒体工作台

文件：`MediaManagerPage.xaml`

关键位置：

- 顶部工具栏约第 177 行；
- 搜索框 `MaxWidth="520"`；
- `SelectedActionsScroller`；
- 图片卡片固定 `Width="160"`；
- 详情面板固定 `Width="250"`。

问题：搜索、筛选、导入和空状态向右越界；详情面板继续挤压图片区；空状态按过宽内容区域居中。

修改要求：

- 宽屏：搜索、筛选、导入同一行；
- 中宽：搜索第一行，筛选与导入第二行；
- 窄屏：次要筛选进入 Flyout；
- 空状态在实际父容器内居中；
- 详情面板在中宽度改到底部或 Flyout；
- 图片卡片由 ItemsWrapGrid 自动决定列数。

验收：分别截图空状态、单选详情、多选工具栏。

### 4. Cleaner 三个内部页面

文件：`CleanerAssistantPage.xaml`

关键位置：

- `SectionRailColumn` 默认宽度 156；
- `MainTabBar`；
- `ScanPrimaryColumn MinWidth="320"`；
- `ScanDetailColumn MinWidth="240"`；
- `SettingsNav`；
- `HistoryNav`；
- 当前 0/1000 响应式断点。

问题：应用导航、Cleaner 内部导航和子页横向标签形成 2–3 层导航；扫描页的全选、空间数字和占用条被裁切；自动化页右侧导入/恢复按钮不可见。

修改要求：

- 使用 Cleaner 页面实际内容宽度驱动断点；
- 中宽度把内部侧栏改成顶部 ComboBox 或 48–56px 图标栏；
- 自动化/记录子导航使用可滚动 Tab 或溢出菜单；
- 磁盘行提供宽/中/窄三种结构，中宽隐藏占用条；
- 右侧详情不足宽度时移到下方或抽屉；
- 不在 code-behind 创建与 ViewModel 并行的状态机。

验收：清理、自动化、记录分别截图；Cleaner 聚焦测试必须通过。

### 5. 设置页

文件：`SettingsPage.xaml`

关键位置：

- `ThemePresetGrid`；
- 外观/AI 服务双列 `1.05*` / `0.95*`；
- MCP 和在线 API 分别固定在 Grid 第 0/1 列。

问题：宽屏主题预设仍单列，浪费空间并拉长页面；内容不足时扩展卡片仍双列，右侧卡片越界。

修改要求：

- 主题预设改成响应式 4/2/1 列；
- 外观/AI 服务与 MCP/在线 API 使用同一断点，不足宽度改为单列；
- 不硬编码 Grid.Column 后再依赖裁剪；
- 大字号时说明和控件必须换行；
- 保存成功/失败反馈不能被滚动区遮挡。

验收：8 个主题选项均可见；Light/Dark/System、8 主题、4 字号和减少动态效果持久化不回归。

### 6. 更新日志列表与详情

文件：`Views/DevLogPage.xaml`

关键位置：

- `MainContentGrid MaxWidth="1100"`；
- 卡片右侧版本、日期和动作区；
- `DetailOverlay`；
- 详情 Grid `MaxWidth="800"`。

问题：添加记录、版本、日期和卡片动作越界；详情关闭入口不可见；覆盖层未在实际视口内闭合。

修改要求：

- 日志卡片 Stretch，所有列允许收缩；
- 中宽度把版本/时间移动到标题下方；
- 动作按钮可折行或进入“更多”菜单；
- 详情面板真正居中并限制可用宽高；
- 关闭按钮固定在面板右上角；
- Overlay scrim 明显压低底层对比度。

验收：列表、详情、输入对话框分别截图；正式历史哈希不变。

### 7. 高级图片编辑器

文件：`Views/Dialogs/AdvancedImageEditorDialog.xaml`

关键位置：

- `ContentDialogMaxWidth=1200`；
- 主 Grid `MinWidth="780" MaxWidth="1080"`；
- 参数栏固定 `Width="360"`。

问题：1440 和 1600 视口均横纵双向越界，参数栏和底部应用/取消不可见。

修改要求：

- 删除阻塞响应式的 MinWidth；
- 宽屏双栏，中宽缩窄参数栏，窄屏单列；
- 内容区单独滚动；
- 标题和动作栏固定；
- 比例按钮必须换行；
- 不修改 AdvancedEditOptions 和图片编辑算法。

验收：1600×900、1280×800、900×700 均能看到“应用并导出”和“取消”。

### 8. 重复/相似结果

文件：`DuplicateResultDialog.xaml`

关键位置：

- 根 Grid `Width="620" MaxWidth="760"`；
- 列表 `MaxHeight="430"`。

问题：右侧和底部越界，主要动作完全不可见。

修改要求：

- 删除固定 Width；
- 列表高度根据可用窗口高度计算；
- 只有列表滚动，标题/说明/动作固定；
- 文件行收紧为选择、缩略图、文件信息三列；
- 相似和完全重复两种模式分别验证。

### 9. 自动增强

文件：`Views/Dialogs/EnhanceImageDialog.xaml`

关键位置：

- ScrollViewer `MaxHeight="560"`；
- StackPanel `Width="440"`；
- 预览固定 `Height="190"`。

问题：内容过高，动作栏不可见。

修改要求：

- 参数改成宽屏 2×2、窄屏单列；
- 中等窗口降低预览高度；
- 内容区滚动，动作栏固定；
- 保留本机处理、不上传提示。

### 10. 更新日志输入

文件：`Views/DevLogInputDialog.xaml`

关键位置：

- ScrollViewer `MaxHeight="580"`；
- StackPanel `Width="600" MaxWidth="720"`。

问题：日期字段和底部保存/取消越界。

修改要求：

- 删除固定 Width；
- 版本/分级/日期使用三列、两行、单列响应式状态；
- 正文使用剩余高度；
- 动作栏固定；
- 添加与编辑两种模式都截图。

### 11. 主题原生强调色

文件：

- `Themes/SharedTheme.xaml`；
- `App.xaml.cs`。

问题：Ochre 主题中 RadioButton、ToggleSwitch、Slider 和对话框取消按钮仍使用系统蓝/青，形成两套主色。

修改要求：

- 确认运行时更新 WinUI 原生控件实际消费的 ThemeResource；
- 必要时在 Light/Dark ThemeDictionaries 覆盖 Accent Brush；
- 避免 StaticResource 锁定启动时系统强调色；
- 抽查 Default、Sky、Ochre、Sepia。

### 12. 对话框取消按钮层级

文件：`Themes/SharedTheme.xaml`

关键位置：

- `SubtleActionButtonStyle`；
- `WorkbenchContentDialogStyle`。

问题：虽然指定了弱按钮样式，实际 ContentDialog 取消按钮仍为高饱和蓝色，与主要动作同权重。

修改要求：

- 检查 ContentDialog CloseButtonStyle 是否被 WinUI 模板资源覆盖；
- 为 Dialog Close button 显式覆盖 Background、Foreground、PointerOver 和 Pressed；
- 取消使用弱表面/透明；
- 普通主要动作使用主题主色，危险主要动作使用危险色；
- 保持 DefaultButton=Close 的安全默认。

## 推荐实施顺序

### Task 1：统一响应式 ContentDialog 契约

先修：

1. 高级图片编辑器；
2. 重复/相似结果；
3. 自动增强；
4. 更新日志输入；
5. 重命名和格式转换的动作栏贴边问题。

每个具体缺陷先增加失败测试，再实现最小修复。

### Task 2：修 MainWindow 和页面根宽度闭包

确保页面按 ContentFrame 实际宽度布局。

### Task 3：逐页修 P1

顺序建议：

1. 媒体；
2. Cleaner；
3. DevLog；
4. Home/AI；
5. Settings。

不要把所有页面一次性修改为一个巨大补丁。

### Task 4：主题和对话框按钮

同步原生 Accent 资源，修复取消按钮权重。

### Task 5：重新截图全部界面

共享主题和 ContentDialog 样式会影响所有界面，因此最终必须重新截图全部页面和对话框，不能只截图直接修改的文件。

## 测试与构建门禁

```powershell
dotnet test .\BlueSapphire.Tests\BlueSapphire.Tests.csproj --no-restore -v:minimal
dotnet build .\BlueSapphire.slnx --no-incremental --no-restore -v:minimal
dotnet build .\BlueSapphire.slnx -c Release --no-incremental --no-restore -v:minimal
```

要求：

- 测试不少于当前 233 项；
- 0 失败；
- Debug/Release 均 0 警告、0 错误。

历史保护：

```powershell
$before = (Get-FileHash -Algorithm SHA256 Assets\DevMatrixLog.json).Hash
# 运行测试和构建
$after = (Get-FileHash -Algorithm SHA256 Assets\DevMatrixLog.json).Hash
if ($before -ne $after) { throw 'DevMatrixLog.json 被修改' }
```

期望哈希：

```text
A9460CC2C03CFED65B36BE74E91D5CD2FBACD978D4243F8E6EFB9C81EBB64FEC
```

## 截图验收门禁

每个界面至少验证：

- 1600×900；
- 1280×800；
- 900×700；
- 标准字号；
- 大字号；
- Light/Dark；
- 至少一个非默认主题；
- 减少动态效果。

每张截图检查：

- Primary、Close、取消是否完整可见；
- 是否有水平越界；
- 是否出现双纵向滚动；
- 文本是否换行；
- 焦点是否清晰；
- 主题强调色是否一致；
- 空状态是否位于真实内容区域中心。

## 完成标准

只有同时满足以下条件，才能再次标记“全 UI 重构完成”：

1. 所有 P0 问题关闭；
2. 1600×900、1280×800、900×700 页面无水平越界；
3. 所有 ContentDialog 的标题、内容、Primary、Close 都在视口内；
4. 工作台长消息正常换行；
5. Media 空状态、工具栏和详情区响应式正常；
6. Cleaner 三个内部页面无裁切；
7. Settings 主题预设为 4/2/1 响应式布局；
8. DevLog 列表、详情和输入对话框完整可用；
9. 非默认主题的原生控件强调色一致；
10. 取消按钮不再与主要动作同权重；
11. 测试不少于 233 项且零失败；
12. Debug/Release 0 警告、0 错误；
13. 全界面截图已经更新；
14. `Assets/DevMatrixLog.json` 哈希不变；
15. Cleaner、AI、Media 业务算法未被修改。

## 下一窗口建议开场指令

```text
继续按 docs/superpowers/plans/2026-07-22-full-ui-screenshot-audit-remediation.md 执行。
第二窗口已完成 DevLog 列表/详情、Home/AI 消息换行、Cleaner 三态内容宽度布局修复并通过回归（240 项，唯一失败为管理员会话环境敏感断言）；下一步先在普通（非管理员）非沙箱会话确认 Debug/Release 无增量构建 0 警告 0 错误并重跑全量测试，再进行 Media、Cleaner、DevLog、Home/AI、Settings 三视口真实截图验收与 Task 5 全界面截图更新。
```
