
# 💎 Blue Sapphire (蓝宝石工具箱)
没问题！既然我们要将项目推进到 **v0.5.1**，GitHub 的 `README.md` 也需要同步更新，以体现架构的专业化和性能的提升。

这是为你优化后的 `README.md` 完整内容。我不仅更新了版本号，还重点突出了 **MVVM 架构**、**网格分区算法**以及**视觉降噪**等核心升级点。

---

# 💎 Blue Sapphire (蓝宝石工具箱)

> **版本**: v0.5.1 Beta (Architecture & Performance Evolution)
> **状态**: ✅ 开发中 / 架构专业化 / 性能质跃
> **风格**: Cyberpunk / HUD / Glassmorphism

**Blue Sapphire** 是一个基于 **Windows 11 (WinUI 3)** 构建的现代化系统工具箱。项目旨在融合 **赛博朋克 (Cyberpunk)** 视觉风格与 **HUD 科技感**，利用 **Win2D** 提供高性能的沉浸式用户体验。

---

## ✨ 核心亮点 (Key Features)

### 1. 🏗️ 专业 MVVM 架构 (v0.5.1 重磅升级)

* **逻辑解耦**: 引入 `CommunityToolkit.Mvvm` 框架，将业务逻辑从 UI 彻底剥离至 `ViewModel` 层，极大提升了代码的可维护性与扩展性。
* **低耦合交互**: 通过 `IMediaViewInteraction` 接口实现了 ViewModel 对 View 层（如弹窗、文件夹选择）的无感知调用。

### 2. 📂 媒体管家 (Media Manager)

专为管理海量图片资源打造的高性能文件管理器：

* **极速性能**:
* **虚拟化技术**: 支持数万张图片的秒级加载与无限滚动。
* **非阻塞索引**: 利用 Windows 系统索引实现瞬开体验。


* **全能排序**: 支持按 **名称 / 日期 / 大小** 进行双向排序。
* **智能去重**:
* **MD5 深度校验**: 仅对大小一致的文件进行哈希比对，确保 100% 准确率。
* **视觉哈希储备**: 已集成 **dHash (视觉哈希)** 算法，支持未来的相似图识别功能。


* **可视化操作**: 具备删除确认机制及实时百分比进度条。

### 3. 🎨 粒子视觉引擎 (v0.5.1 性能优化)

* **网格分区算法 (Spatial Partitioning)**: 粒子连线检测复杂度从  优化至 ，确保高负载下依然丝滑。
* **交互降噪**: 背景画布支持 **点击穿透 (Hit-Test Disabled)**，彻底解决装饰背景干扰功能操作的问题。
* **视觉增强**: 引入半透明黑色遮罩（#CC000000），在保留粒子动态氛围的同时，极大提升了前景文字的可读性。

---

## 🛠️ 技术栈 (Tech Stack)

* **框架**: .NET 8.0 / Windows App SDK (WinUI 3)
* **模式**: MVVM (CommunityToolkit.Mvvm)
* **渲染**: Win2D (Microsoft.Graphics.Win2D)
* **图像处理**: SixLabors.ImageSharp (用于视觉哈希计算)

---

## 📂 项目结构 (Structure)

```text
BlueSapphire/
├── ViewModels/              # [v0.5.1] 核心业务大脑
├── Helpers/                 # 通用辅助类库
│   ├── FileHelper.cs              # MD5 与 dHash 核心算法
│   ├── AppSettings.cs             # JSON 配置持久化
│   └── Converters.cs              # XAML 数据转换器
├── Interfaces/              # 插件与交互标准接口
├── Models/                  # 数据模型 (ImageItem, DuplicateItem)
├── Pages/                   # UI 页面 (已全面适配半透明遮罩)
├── MainWindow.xaml          # 宿主窗口 (集成网格分区粒子引擎)
└── ...

```

---

## 🚀 快速开始 (Getting Started)

### 环境要求

* Windows 10 (19041) 或 Windows 11
* Visual Studio 2022 (需安装 "Windows App SDK C# 模板")
* .NET 8 SDK

### 构建步骤

1. 克隆仓库: `git clone https://github.com/YourUsername/BlueSapphire.git`
2. 在 VS 中打开 `BlueSapphire.slnx`。
3. 还原 NuGet 包（包含 `CommunityToolkit.Mvvm`, `Win2D`, `ImageSharp`）。
4. 选择 `x64` 平台进行构建并运行。

---

## 🗺️ 开发路线图 (Roadmap)

### ✅ 已完成 (v0.1 - v0.5.1)

* [x] **MVVM 架构重构**: 逻辑与 UI 彻底解耦。
* [x] **性能优化**: 引入空间分区算法优化粒子网络。
* [x] **交互升级**: 点击穿透背景与视觉遮罩层。
* [x] **媒体管家**: 排序、MD5 去重、删除进度可视化。

### 🚧 规划中 (v0.6+)

* [ ] **相似图片聚合**: 基于已集成的 dHash 算法实现相似识别。
* [ ] **图片详情查看器 (Immersive Viewer)**: 全屏查看，支持缩放与导航。
* [ ] **回收站机制**: 接入 Windows 回收站 API，提升安全性。

---

Copyright © 2025-2026 BlueSapphire Team.
