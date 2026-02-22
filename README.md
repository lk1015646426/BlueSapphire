# 💎 Blue Sapphire (蓝宝石工具箱)

> **版本**: v0.6.0 (The Matrix Evolution)
> **状态**: ✅ 架构重构 / 视觉升级 / 零 GC 渲染 / 工业级持久化
> **风格**: Cyberpunk / Command Center / Glassmorphism 2.0
> **生态**: .NET 8.0 / Windows App SDK (WinUI 3)

**Blue Sapphire** 是一个基于 **Windows 11 (WinUI 3)** 构建的现代化系统工具箱。本项目致力于打破传统工具软件的刻板印象，融合硬核的**赛博朋克 (Cyberpunk)** 视觉风格与极致的底层性能，为用户提供兼具 HUD 科技感与实用性的沉浸式交互体验。

随着 v0.7.0 版本的发布，项目引入了“跃迁记录 (Matrix DevLog)”系统，重写了底层数据的安全持久化逻辑，并实现了纯 C# 驱动的极简微动效，标志着 Blue Sapphire 在架构严谨性与交互质感上达到了全新的高度。

---

## ✨ 核心亮点 (Key Features)

### 1. 🎨 赛博视觉与零渲染开销 (Visual & Rendering)
* **HUD 指挥中心**: 采用基于 `Microsoft.Graphics.Canvas.UI.Xaml.CanvasControl` 构建的硬件加速渲染层，提供带有光标交互响应 (`PointerMoved`) 的深邃动态背景。
* **沉浸式排版**: 深度定制的透明标题栏与超宽字符间距 (`CharacterSpacing="100"`) 营造出独特的终端指令操作感。
* **零 GC 粒子引擎**: 通过对象池 (Object Pooling) 和手动渲染循环重构粒子系统 (`Particle.cs`)，将高频绘制时的内存分配与垃圾回收 (GC) 压力降至 **0**，在数千粒子同屏下依然保持丝滑流畅。
* **Cyber Pulse 赛博心跳**: 弃用臃肿的 XAML 动画，全量采用纯 C# 构建底层 `Storyboard` 与阻尼缓动函数 (`SineEase`)，配合 `PointerPressed` 硬件级事件，实现零延迟防吞噬的“微回弹”极客交互动效。

### 2. 📂 工业级媒体管家 (Media Manager)
* **智能时空重构 (Smart Chrono-Rename)**:
  * 内置高精度时间解析引擎，支持 1900-2099 年份的泛时间识别。
  * 通过多组严密的正则表达式，精准提取混杂在杂乱文件名中的年月日时分秒信息，并将其标准化为统一的时间轴格式。
* **三级去重扫描算法 (Tiered Deduplication)**:
  * **Tier 1 (极速分组)**: 绕过繁重的对象实例化，利用底层 `FileInfo` 获取文件字节大小，进行第一轮 O(N) 规模的嫌疑分组。
  * **Tier 2 (头尾哈希)**: 针对同尺寸文件，仅抽取文件头尾 4KB 区块进行轻量级 Hash 计算，快速过滤掉绝大多数结构不同的文件。
  * **Tier 3 (全量 MD5)**: 仅对前两级判定为极度疑似的样本组进行全量的 MD5 深度校验，在保证 100% 准确率的同时将 I/O 耗时降至最低。

### 3. 📜 矩阵跃迁记录 (Matrix DevLog)
* **原子化数据持久化**: 底层 IO 写入采用 `SemaphoreSlim` 线程锁与 `.tmp` 临时文件机制，在写入完成后瞬间原位替换 (`File.Move`)，彻底规避因程序崩溃或断电导致的数据文件损坏。
* **环境自适应存储**: 深度适配 WinUI 3 `Unpackaged` (未打包) 运行环境，智能绕过沙盒限制，安全调用 `LocalApplicationData` 构建本地数据库。
* **防截断内存直读**: 突破 UI 控件的渲染瓶颈，万字长文更新文档可直接通过 `.txt` / `.md` 导入内存变量，实现无损展示。

### 4. 🏗️ 现代化底层架构 (Modern Architecture)
* **.NET 8 与前沿标准**: 核心框架运行于 `.net8.0-windows10.0.19041.0`，全面享受最新的 C# 性能红利。
* **AOT 绝对兼容**: 项目移除了常规的 `[ObservableProperty]` 动态生成代码，全面采用严格的手写 MVVM 属性绑定规范，扫清了 WinRT 环境下的序列化障碍，为开启 `PublishAot` 彻底原生化编译做好了万全准备。
* **MVVM 彻底解耦**: 摒弃单例强耦合，全面引入 `CommunityToolkit.Mvvm` 的 `WeakReferenceMessenger` (弱引用消息总线) 跨组件调度状态，实现 UI 视图层与业务逻辑层的绝对隔离。

---

## 📂 项目结构 (Structure)

本项目的代码组织严格遵循现代化 MVVM 架构、高内聚低耦合的工程规范及 AOT 兼容标准：

```text
BlueSapphire/
├── Assets/                  # 静态资源 (Logo, Splashes, Icons)
├── Helpers/                 # 辅助工具类
│   ├── AppSettings.cs       # 本地配置存储 (LocalSettings)
│   └── IncrementalLoading...# 增量加载集合 (支持无限滚动列表)
├── Interfaces/              # 抽象接口定义 (UI 解耦与插件化契约)
│   ├── IMediaViewInteraction.cs 
│   └── ITool.cs             # 工具插件标准接口
├── Models/                  # 数据实体模型 (AOT 兼容规范)
│   ├── AppMessages.cs       # 消息总线定义 (WeakReferenceMessenger)
│   ├── DevLogItem.cs        # 跃迁记录节点实体
│   ├── DuplicateItem.cs     # 重复文件分组实体 (含缩略图懒加载)
│   ├── ImageItem.cs         # 媒体文件基础实体
│   └── RenamePreviewItem.cs # 重命名预览实体
├── Services/                # 核心底层服务 (算法与持久化层)
│   ├── DevLogDataService.cs         # 原子化 JSON 持久化服务
│   ├── MediaDeduplicationService.cs # 三级去重扫描算法核心实现
│   ├── MediaRenameService.cs        # 泛时间正则解析与重命名引擎
│   ├── MediaScanService.cs          # 头尾哈希计算与文件扫描底层服务
│   └── NativeFileService.cs         # Win32 Shell API 封装 (回收站安全删除)
├── Tools/                   # 工具策略层实现
│   ├── DevLogTool.cs        # 跃迁记录工具定义
│   ├── HomeTool.cs          # 首页工具定义
│   └── MediaManagerTool.cs  # 媒体管家工具定义
├── ViewModels/              # 业务逻辑层 (严谨的 MVVM 数据驱动)
│   ├── DevLogViewModel.cs       # 日志数据流转与状态管理
│   └── MediaManagerViewModel.cs # 媒体管理核心逻辑与多线程调度
├── Views/                   # UI 视图层 (Pages & Dialogs)
│   ├── HomePage.xaml        # 赛博指挥中心首页 (HUD 风格)
│   ├── DevLogPage.xaml      # 跃迁记录展示页 (时间轴)
│   ├── MediaManagerPage.xaml# 媒体网格展示与交互页
│   ├── SettingsPage.xaml    # 全局设置页 (含隐藏彩蛋入口)
│   ├── AboutPage.xaml       # 关于页面
│   ├── DevLogInputDialog.xaml     # 记录添加与 TXT 导入弹窗
│   ├── DuplicateResultDialog.xaml # 去重结果确认可视化弹窗
│   └── RenamePreviewDialog.xaml   # 重命名操作沙盒预览弹窗
├── MainWindow.xaml          # 主窗口 (含 CanvasControl 硬件加速渲染与导航)
├── Particle.cs              # 粒子物理与渲染实体 (零 GC 优化核心)
├── App.xaml                 # 应用生命周期与全局资源管理
├── BlueSapphire.csproj      # 项目配置文件 (AOT/R2R 设定、WinUI 3 依赖)
└── Package.appxmanifest     # MSIX 应用清单与权限配置
