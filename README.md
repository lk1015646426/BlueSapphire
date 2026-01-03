💎 Blue Sapphire (蓝宝石工具箱)
版本: v0.6.0 (The Visual Revolution) 状态: ✅ 架构重构完成 / 视觉升级 / 零 GC 渲染 风格: Cyberpunk / Command Center / Glassmorphism 2.0

Blue Sapphire 是一个基于 Windows 11 (WinUI 3) 构建的现代化系统工具箱。项目旨在融合 赛博朋克 (Cyberpunk) 视觉风格与 HUD 科技感，打造极致性能的沉浸式体验。

v0.6.0 版本 带来了革命性的视觉与底层更新：引入了 “指挥中心”首页，重写了粒子渲染引擎实现 零内存分配 (Zero-Allocation)，并通过 消息总线 (Messenger) 彻底解耦了架构，标志着项目从“可用”迈向“工业级精品”。

✨ 核心亮点 (Key Features)
1. 🎨 视觉革命 (Visual Revolution)
赛博指挥中心 (Command Center): 全新的 HUD 风格首页，采用非对称布局与科技感排版，提供沉浸式的启动体验。

玻璃拟态 2.0 (Glassmorphism): 深度定制的亚克力 (Acrylic) 半透明材质，配合 动态光效同步 (Sync Glow)——鼠标悬停时边框与背景会产生呼吸般的光律响应。

全动态交互: 集成平滑的 入场动画 (Entrance Storyboard) 与光标闪烁特效，让界面“活”起来。

深度汉化: 全界面采用高格调的中文科技术语（如“安全协议：已激活”），营造浓厚的科幻氛围。

2. ⚡ 极致引擎 (Extreme Engine)
零内存分配 (Zero-Allocation): 引入 对象池 (Object Pooling) 技术重构粒子系统，将高频渲染时的 GC (垃圾回收) 压力降至 0，彻底消除微卡顿。

手动高性能循环: 摒弃封装控件，基于 CompositionTarget.Rendering 手动构建 60FPS+ 游戏级渲染循环，结合 空间分区算法 (Spatial Partitioning)，在数千粒子下依然丝滑流畅。

启动瞬开: 移除反射 (Reflection) 扫描，采用 手动依赖注入 注册工具链，配合 ReadyToRun (R2R) 技术，实现毫秒级启动。

3. 🏗️ 现代架构 (Modern Architecture)
彻底解耦: 移除所有 Singleton 强耦合，全面采用 CommunityToolkit.Mvvm 的 WeakReferenceMessenger (弱引用消息总线) 进行跨组件通信（如设置页控制主窗口特效）。

AOT 就绪: 代码结构全面适配 .NET Native AOT 标准，移除了动态特性依赖，为未来的原生编译铺平道路。

稳健性: 修复了 WinUI 3 中 ThemeShadow 和 Triggers 可能导致的渲染层崩溃问题，稳定性达到企业级标准。

4. 📂 媒体管家 (Media Manager)
智能时空重构 (Smart Chrono-Rename):

采用 三级解析策略 (Tiered Parsing Strategy)：优先读取 Exif 元数据，回退至 正则文件名智能分析，并自动拦截无意义字符（如 mmexport... 等伪时间数据）。

将混乱的文件名标准化为 yyyy-MM-dd_HH-mm-ss 时间轴格式，并内置 自动序列化 冲突解决机制。

可视化去重协议 (Visual Deduplication):

集成 MD5 深度校验 与视觉哈希算法。

在执行删除指令前，提供 嫌疑文件视觉预览 (Suspect Visual Preview)，支持在删除列表中直接查看图片内容，确保“所见即所删”，彻底杜绝误杀风险。

极速虚拟化: 支持数万张图片的秒级加载与无限滚动。

安全交互: 具备跨线程安全的实时进度条与操作确认机制。

🛠️ 技术栈 (Tech Stack)
核心: .NET 8 (LTS) / Windows App SDK (WinUI 3)

架构: MVVM (CommunityToolkit.Mvvm) / Dependency Injection (Manual)

渲染: Microsoft.Graphics.Win2D / CompositionTarget.Rendering

特效: Custom Particle Engine (Object Pooling + Spatial Partitioning)

图像: SixLabors.ImageSharp

📂 项目结构 (Structure)
Plaintext

BlueSapphire/
├── ViewModels/              # 业务逻辑 (MVVM)
├── Models/                  
│   ├── AppMessages.cs       # [v0.6.0] 消息总线定义
│   └── ...                  # 数据模型
├── Helpers/                 
│   ├── AppSettings.cs       # 配置管理
│   └── ...
├── Services/                # 核心服务 (文件扫描/哈希计算)
├── Pages/                   
│   ├── HomePage.xaml        # [v0.6.0] 赛博朋克指挥中心首页
│   ├── MediaManagerPage.xaml# 媒体管理功能页
│   └── SettingsPage.xaml    # 设置页
├── MainWindow.xaml.cs       # [v0.6.0] 零GC粒子引擎 + 手动渲染循环
└── BlueSapphire.csproj      # .NET 8 + WinUI 3 配置

"System Online. Protocol Activated."
