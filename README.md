# 💎 Blue Sapphire (蓝宝石工具箱)

> **版本**: v0.5.2 (The Stable Foundation)
> **状态**: ✅ 开发中 / 架构稳健 / 体验流畅
> **风格**: Cyberpunk / HUD / Glassmorphism

**Blue Sapphire** 是一个基于 **Windows 11 (WinUI 3)** 构建的现代化系统工具箱。项目旨在融合 **赛博朋克 (Cyberpunk)** 视觉风格与 **HUD 科技感**，利用 **Win2D** 提供高性能的沉浸式用户体验。

v0.5.2 版本回归至企业级稳健的 **.NET 8 (LTS)** 核心，并采用 **ReadyToRun (R2R)** 预编译技术，在保持极速启动的同时，提供了最佳的系统兼容性与稳定性。

-----

## ✨ 核心亮点 (Key Features)

### 1. 🏗️ 专业 MVVM 架构 (Architecture)
* **逻辑解耦**: 引入 `CommunityToolkit.Mvvm` 框架，将业务逻辑从 UI 彻底剥离至 `ViewModel` 层，极大提升了代码的可维护性与扩展性。
* **低耦合交互**: 通过 `IMediaViewInteraction` 接口实现了 ViewModel 对 View 层（如弹窗、文件夹选择）的无感知调用。

### 2. 🚀 极致性能 (Performance)
* **极速启动**: 采用 **ReadyToRun (R2R)** 预编译技术，结合 WinUI 3 原生渲染，实现了接近原生应用的启动速度。
* **平滑体验**: 针对 .NET 8 LTS 运行时进行了深度优化，内存占用更低，运行更稳定，杜绝了 AOT 模式下的兼容性闪退问题。

### 3. 📂 媒体管家 (Media Manager)
专为管理海量图片资源打造的高性能文件管理器：
* **极速虚拟化**: 支持数万张图片的秒级加载与无限滚动。
* **全能排序**: 支持按 **名称 / 日期 / 大小** 进行双向排序。
* **智能去重**:
    * **MD5 深度校验**: 仅对大小一致的文件进行哈希比对，确保 100% 准确率。
    * **视觉哈希储备**: 集成 **dHash (视觉哈希)** 算法，为 v0.6 的相似图识别奠定基础。
* **可视化操作**: 具备删除确认机制及跨线程安全的实时进度条。

### 4. 🎨 粒子视觉引擎 2.0 (Visuals)
* **空间分区算法 (Spatial Partitioning)**: 引入网格算法，将粒子连线检测复杂度从 $O(N^2)$ 优化至 $O(N)$，确保高负载下依然保持 60FPS 丝滑帧率。
* **交互降噪**: 背景画布支持 **点击穿透 (Hit-Test Disabled)**，彻底解决装饰背景干扰功能操作的问题。
* **视觉增强**: 引入半透明黑色遮罩 (`#CC000000`)，在保留粒子动态氛围的同时，极大提升了前景文字的可读性。

-----

## 🛠️ 技术栈 (Tech Stack)

* **IDE**: Visual Studio 2022
* **框架**: .NET 8 (LTS) / Windows App SDK (WinUI 3)
* **编译**: ReadyToRun (R2R)
* **模式**: MVVM (CommunityToolkit.Mvvm)
* **渲染**: Win2D (Microsoft.Graphics.Win2D)
* **图像处理**: SixLabors.ImageSharp

-----

## 📂 项目结构 (Structure)

```text
BlueSapphire/
├── ViewModels/              # [v0.5.2] 核心业务大脑 (MVVM)
├── Helpers/                 
│   ├── FileHelper.cs              # MD5 与 dHash 核心算法
│   ├── AppSettings.cs             # 强类型配置管理
│   └── Converters.cs              # XAML 数据转换器
├── Interfaces/              # 插件与交互标准接口
├── Models/                  # 数据模型 (ImageItem, DuplicateItem)
├── Pages/                   # UI 页面 (已全面适配半透明遮罩)
├── MainWindow.xaml.cs       # [v0.5.2] 空间分区粒子引擎 + 动态依赖注入
└── BlueSapphire.csproj      # [v0.5.2] .NET 8 LTS + WinUI 3 核心配置
