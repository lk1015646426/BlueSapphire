# 💎 Blue Sapphire (蓝宝石工具箱)

> **版本**: v0.6.0 (The Visual Revolution)
> **状态**: ✅ 架构重构完成 / 视觉升级 / 零 GC 渲染
> **风格**: Cyberpunk / Command Center / Glassmorphism 2.0
> **生态**: .NET 8.0 / Windows App SDK (WinUI 3)

**Blue Sapphire** 是一个基于 **Windows 11 (WinUI 3)** 构建的现代化系统工具箱。本项目致力于打破传统工具软件的刻板印象，融合硬核的**赛博朋克 (Cyberpunk)** 视觉风格与极致的底层性能，为用户提供兼具 HUD 科技感与实用性的沉浸式交互体验。

随着 v0.6.0 版本的发布，项目引入了“指挥中心”首页，重写了粒子渲染引擎，并彻底解耦了底层架构，标志着 Blue Sapphire 正式从“可用”迈向“工业级精品”。

---

## ✨ 核心亮点 (Key Features)

### 1. 🎨 赛博视觉与零渲染开销 (Visual & Rendering)
* **HUD 指挥中心**: 采用基于 `Microsoft.Graphics.Canvas.UI.Xaml.CanvasControl` 构建的硬件加速渲染层，提供带有光标交互响应 (`PointerMoved`) 的深邃动态背景。
* **沉浸式排版**: 深度定制的透明标题栏与超宽字符间距 (`CharacterSpacing="100"`) 营造出独特的终端指令操作感。
* **零 GC 粒子引擎**: 通过对象池 (Object Pooling) 和手动渲染循环重构粒子系统 (`Particle.cs`)，将高频绘制时的内存分配与垃圾回收 (GC) 压力降至 **0**，在数千粒子同屏下依然保持丝滑流畅。

### 2. 📂 工业级媒体管家 (Media Manager)
* **智能时空重构 (Smart Chrono-Rename)**:
  * 内置高精度时间解析引擎，支持 1900-2099 年份的泛时间识别。
  * 通过多组严密的正则表达式，精准提取混杂在杂乱文件名中的年月日时分秒信息，并将其标准化为统一的时间轴格式。
* **三级去重扫描算法 (Tiered Deduplication)**:
  * **Tier 1 (极速分组)**: 绕过繁重的对象实例化，利用底层 `FileInfo` 获取文件字节大小，进行第一轮 O(N) 规模的嫌疑分组，大幅提升初筛速度。
  * **Tier 2 (头尾哈希)**: 针对同尺寸文件，仅抽取文件头尾区块进行轻量级 Hash 计算，快速过滤掉绝大多数结构不同的文件。
  * **Tier 3 (全量 MD5)**: 仅对前两级判定为极度疑似的样本组进行全量深度的 MD5 校验，在保证 100% 准确率的同时将 I/O 耗时降至最低。

### 3. 🏗️ 现代化底层架构 (Modern Architecture)
* **.NET 8 与前沿标准**: 核心框架运行于 `.net8.0-windows10.0.19041.0`，全面享受最新的 C# 性能红利。
* **AOT 就绪与 R2R**: 项目已开启 `PublishReadyToRun` 优化启动速度，移除了反射与动态代码依赖，为未来的 `PublishAot` 彻底原生化编译做好了准备。
* **MVVM 彻底解耦**: 摒弃单例强耦合，全面引入 `CommunityToolkit.Mvvm` 的 `WeakReferenceMessenger` (弱引用消息总线) 跨组件调度状态，实现 UI 视图层与业务逻辑层的绝对隔离。

---

## 📂 项目结构 (Structure)

本项目的代码组织严格遵循现代化 MVVM 架构与高内聚低耦合的工程规范：

```text
BlueSapphire/
├── Assets/                  # 静态资源 (Logo, Splashes, Icons)
├── Helpers/                 # 辅助工具类
│   ├── AppSettings.cs       # 本地配置存储 (LocalSettings)
│   ├── Converters.cs        # XAML 值转换器 
│   └── IncrementalLoading...# 增量加载集合 (支持无限滚动列表)
├── Interfaces/              # 抽象接口定义 (UI 解耦与插件化契约)
│   ├── IMediaViewInteraction.cs 
│   └── ITool.cs             # 工具插件标准接口
├── Models/                  # 数据实体模型
│   ├── AppMessages.cs       # 消息总线定义 (WeakReferenceMessenger)
│   ├── DuplicateItem.cs     # 重复文件分组实体
│   ├── ImageItem.cs         # 媒体文件基础实体
│   └── RenamePreviewItem.cs # 重命名预览实体
├── Services/                # 核心底层服务 (算法与原生 API)
│   ├── MediaDeduplicationService.cs # 三级去重扫描算法核心实现
│   ├── MediaRenameService.cs        # 泛时间正则解析与重命名引擎
│   ├── MediaScanService.cs  # 哈希计算与文件扫描底层服务
│   └── NativeFileService.cs # Win32 Shell API 封装 (实现安全回收站删除)
├── Tools/                   # 工具策略层实现
│   ├── HomeTool.cs          # 首页工具定义
│   └── MediaManagerTool.cs  # 媒体管家工具定义
├── ViewModels/              # 业务逻辑层 (基于 CommunityToolkit.Mvvm)
│   └── MediaManagerViewModel.cs # 媒体管理核心逻辑调度
├── Views/                   # UI 视图层 (Pages & Dialogs)
│   ├── HomePage.xaml        # 赛博指挥中心首页 (HUD 风格)
│   ├── MediaManagerPage.xaml# 媒体网格展示与交互页
│   ├── SettingsPage.xaml    # 全局设置页
│   ├── AboutPage.xaml       # 关于页面
│   ├── DuplicateResultDialog.xaml # 去重结果确认可视化弹窗
│   └── RenamePreviewDialog.xaml   # 重命名操作沙盒预览弹窗
├── MainWindow.xaml          # 主窗口 (含 CanvasControl 硬件加速渲染循环与导航)
├── Particle.cs              # 粒子物理与渲染实体 (零 GC 优化核心)
├── App.xaml                 # 应用生命周期与全局资源管理
├── BlueSapphire.csproj      # 项目配置文件 (AOT/R2R 设定、WinUI 3 依赖)
└── Package.appxmanifest     # MSIX 应用清单与权限配置
