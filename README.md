
# 💎 Blue Sapphire (蓝宝石工具箱)

> **版本**: v0.5.0 Beta (Media Manager Evolution)
> **状态**: ✅ 开发中 / 架构稳定
> **风格**: Cyberpunk / HUD / Glassmorphism

**Blue Sapphire** 是一个基于 **Windows 11 (WinUI 3)** 构建的现代化系统工具箱。项目旨在融合 **赛博朋克 (Cyberpunk)** 视觉风格与 **HUD 科技感**，利用 **Win2D** 提供高性能的沉浸式用户体验，摆脱传统 WinForm/WPF 工具箱的陈旧感。

-----

## ✨ 核心亮点 (Key Features)

### 1\. 🧬 宿主+插件架构 (Host-Plugin Architecture)

  * **反射式加载**: 采用高度解耦的架构，宿主 (`MainWindow`) 仅负责窗口管理和背景渲染。
  * **动态发现**: 系统启动时通过 `System.Reflection` 自动扫描并加载所有实现 `ITool` 接口的功能模块。

### 2\. 📂 媒体管家 (Media Manager) - *v0.5 核心升级*

专为管理海量图片资源打造的高性能文件管理器：

  * **极速性能**:
      * **虚拟化技术**: 实现 `IncrementalLoadingCollection` 和 `ISupportIncrementalLoading`，支持数万张图片的秒级加载与无限滚动。
      * **非阻塞索引**: 利用 Windows 系统索引 (`StorageFileQueryResult`) 替代传统遍历，实现瞬开体验。
  * **全能排序**: 支持按 **名称 / 日期 / 大小** 进行升序或降序排列。
  * **智能去重**:
      * 基于 **MD5 深度校验**，精准识别内容重复的文件。
      * 提供可视化结果清单与批量删除功能。
  * **沉浸式交互**:
      * 加载时采用 **Overlay 遮罩层**，保持界面呼吸感。
      * 删除操作配备实时百分比进度条 (HUD 风格)。
      * 文件大小自动格式化显示 (KB/MB/GB)。

### 3\. 🎨 视觉与交互 (Visual & UX)

  * **粒子神经网络**: 基于 **Win2D** 渲染的高性能动态背景，包含 100+ 物理粒子、动态连线及鼠标斥力场交互。
  * **HUD 风格**: 全局采用高对比度文本、霓虹光晕边框及玻璃拟态 (Glassmorphism) 卡片设计。
  * **无边框窗口**: 自定义标题栏，内容延伸至顶部，提供极致的沉浸感。

-----

## 🛠️ 技术栈 (Tech Stack)

  * **框架**: .NET 8.0 / Windows App SDK (WinUI 3) 1.6+
  * **语言**: C\# 12 (Async/Await, Nullable Reference Types)
  * **渲染**: Win2D (Microsoft.Graphics.Win2D) - 用于粒子特效
  * **IDE**: Visual Studio 2022

-----

## 📂 项目结构 (Structure)

```text
BlueSapphire/
├── Helpers/                 # 通用辅助类库
│   ├── AppSettings.cs             # JSON 配置持久化 (替代 LocalSettings)
│   ├── FileHelper.cs              # MD5 计算与文件操作
│   └── IncrementalLoadingCollection.cs # 支持虚拟化无限滚动的核心集合类
├── Interfaces/
│   └── ITool.cs             # 插件标准接口 (定义 Title, Icon, Id)
├── Pages/
│   ├── HomePage.xaml        # 极简仪表盘 (仅保留粒子背景)
│   ├── MediaManagerPage.xaml# 媒体管家 (核心业务逻辑：排序、去重、渲染)
│   └── SettingsPage.xaml    # 设置中心 (粒子开关、版本信息)
├── MainWindow.xaml          # 宿主窗口 (Win2D 画布, 导航路由)
├── Particle.cs              # 粒子物理模型
└── ...
```

-----

## 🚀 快速开始 (Getting Started)

### 环境要求

  * Windows 10 (19041) 或 Windows 11
  * Visual Studio 2022 (需安装 "Windows App SDK C\# 模板")
  * .NET 8 SDK

### 构建步骤

1.  克隆仓库: `git clone https://github.com/YourUsername/BlueSapphire.git`
2.  在 VS 中打开 `BlueSapphire.slnx`。
3.  还原 NuGet 包 (主要依赖 `Microsoft.WindowsAppSDK` 和 `Microsoft.Graphics.Win2D`)。
4.  选择 `x64` 平台进行构建并运行。

-----

## 🗺️ 开发路线图 (Roadmap)

### ✅ 已完成 (v0.1 - v0.5)

  * [x] 基础 Host-Plugin 架构搭建
  * [x] Win2D 粒子神经网络背景
  * [x] 设置状态持久化 (JSON)
  * [x] 媒体管家：虚拟化列表与增量加载
  * [x] 媒体管家：排序系统 (Name/Date/Size)
  * [x] 媒体管家：MD5 智能去重与文件删除

### 🚧 规划中 (v0.6+)

  * [ ] **图片详情页 (Immersive Viewer)**: 全屏图片查看，支持缩放、旋转及键盘导航。
  * [ ] **回收站机制 (Recycle Bin)**: 接入 Windows 回收站 API，替代永久删除，保障数据安全。
  * [ ] **新插件**: 计划添加系统清理或开发者工具类插件。

-----

## 📄 许可证 (License)

本项目采用 MIT 许可证。

-----

Copyright © 2025 BlueSapphire Team.
