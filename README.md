# BlueSapphire

BlueSapphire 是一个面向 Windows 11 的 WinUI 3 工具箱，当前已经包含两条核心产品线：

- `媒体管家`：图片、音频、文档的扫描、整理、预览与批处理
- `清理助手`：空间分析、风险分层、恢复、排除、提权清理、规则热更新

项目目标不是做“堆功能的工具集合”，而是做一套风格统一、交互清晰、可长期演进的桌面工具平台。

## 当前模块

- `主页`：工具导航与整体状态入口
- `媒体管家`：媒体扫描、去重、时间重命名、文档转换、音频预览/裁剪/标签编辑
- `清理助手`：快速扫描、深度扫描、空间分析、隔离恢复、自动低风险保洁、规则治理
- `设置`：应用行为与视觉项配置
- `开发日志`：内部调试与版本记录页

## 技术栈

- `.NET 8`
- `WinUI 3 / Windows App SDK`
- `CommunityToolkit.Mvvm`
- `PDFsharp`
- `TagLibSharp`

运行目标：

- `Windows 11 x64`
- 发布方式为 `win-x64 self-contained`

## 开发环境

本地开发需要：

- `Visual Studio 2022`
- `.NET 8 SDK`
- `Windows App SDK` 开发环境

如果只是安装最终版本，不需要额外安装 `.NET SDK` 或 `Windows App SDK`。

## 本地开发

### 构建

```powershell
dotnet build BlueSapphire.slnx
```

### 测试

```powershell
dotnet test BlueSapphire.Tests\BlueSapphire.Tests.csproj
```

### 发布

```powershell
dotnet publish BlueSapphire.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:WindowsPackageType=None
```

## 安装包发布链

当前仓库只支持一条正式发布链：

1. `dotnet publish` 生成纯净发布目录
2. `Inno Setup` 只对发布目录打包
3. GitHub Release 只上传安装包

### 重要约束

- [installer.iss](C:/Users/10156/Desktop/蓝宝石工具开发/new/BlueSapphire/installer.iss) 是 `publish-only`
- 必须通过 `/dSourcePath=<publish目录>` 显式传入发布目录
- 不允许直接把仓库根目录传给 `installer.iss`
- 如果 `SourcePath` 包含 `BlueSapphire.Tests`、`TestData`、`.git`、`obj`、`bin\Debug` 等内容，编译会直接失败

### 本地打包

```powershell
ISCC.exe `
  /dSourcePath="C:\path\to\publish" `
  /dMyAppName="BlueSapphire" `
  /dMyAppVersion="1.0.0" `
  /dMyAppPublisher="BlueSapphire Team" `
  /dMyAppId="{{8D43FBFA-A424-4FED-BDE6-6C586D7D13EE}" `
  installer.iss
```

### GitHub Release

仓库已内置发布工作流：

- [release.yml](C:/Users/10156/Desktop/蓝宝石工具开发/new/BlueSapphire/.github/workflows/release.yml)

触发方式：

- 推送 `v*` tag
- 手动触发 `workflow_dispatch`

工作流会自动：

- 安装 `.NET 8 SDK`
- 安装 `Inno Setup 6`
- 执行 `dotnet publish`
- 用 `installer.iss` 生成安装包
- 将安装包上传到 GitHub Release

## 项目结构

```text
BlueSapphire/
├── .github/workflows/          # GitHub Actions
├── Assets/                     # 运行时资源、规则、图标
├── BlueSapphire.Tests/         # 单元测试与回归测试
├── Helpers/                    # 配置与通用辅助
├── Interfaces/                 # UI/服务交互接口
├── Models/                     # 业务模型
├── Services/                   # 扫描、清理、媒体、文档、音频等服务
├── Tools/                      # 导航工具定义
├── ViewModels/                 # MVVM 视图模型
├── Views/                      # 页面与对话框
├── TestData/                   # 测试样本
├── installer.iss               # publish-only 安装脚本
└── BlueSapphire.csproj         # 主项目
```

## 说明

- `Assets\CleanerRules.json` 属于正式运行时资源，会跟随发布
- `BlueSapphire.Tests` 和 `TestData` 仅用于开发与验证，不进入正式发布目录
- 如果你想一键构建安装包，配套工具仓库是 `BlueSapphire-Builder`

## 配套仓库

- `BlueSapphire-Builder`：本地一键发布工具，负责执行 `dotnet publish -> ISCC -> 安装包`

## 截图展示

当前仓库已经具备正式 README 截图区结构，但还没有同步放入一组最终版界面截图。

现有可直接展示的仓库素材：

![BlueSapphire Logo](Assets/StoreLogo.png)

建议后续把正式截图放到 `docs/screenshots/`，例如：

- `docs/screenshots/home.png`
- `docs/screenshots/media-manager.png`
- `docs/screenshots/cleaner-assistant.png`

这样 README 可以稳定展示首页、媒体管家、清理助手三张主图，而不会混入调试过程截图。

## 安装包下载说明

正式安装包统一通过 GitHub Releases 分发：

- 下载地址：[BlueSapphire Releases](https://github.com/lk1015646426/BlueSapphire/releases)

下载建议：

- 普通用户直接下载最新版本里的 `BlueSapphire_Setup_v*.exe`
- 安装目标机器不需要提前安装 `.NET SDK`
- 安装目标机器不需要提前安装 `Windows App SDK`

如果你是开发者，需要源码构建而不是安装包：

```powershell
git clone https://github.com/lk1015646426/BlueSapphire.git
cd BlueSapphire
dotnet build BlueSapphire.slnx
```

## 常见问题 FAQ

### 1. 为什么安装包不再支持直接从仓库根目录打包？

因为正式发布现在强制走 `publish-only` 链路。这样可以避免把 `BlueSapphire.Tests`、`TestData`、`.git`、`obj` 等开发内容一起误打进安装包。

### 2. 安装后还需要额外安装运行环境吗？

不需要。当前正式发布目标是 `win-x64 self-contained`，目标机器不需要额外安装 `.NET SDK`。

### 3. 清理助手为什么有些功能会要求管理员权限？

系统级临时目录、更新缓存、错误报告缓存等路径受权限限制。清理助手默认保守，只有进入管理员模式后才允许处理这些目录。

### 4. 清理助手会直接永久删除所有内容吗？

不会。当前设计强调风险分层、恢复和排除。中风险对象优先走隔离或保守处理，不是“全盘通杀”式删除。

### 5. 媒体管家的文档转换为什么在有些机器上不可用？

文档转换依赖本机可用的 Office 或 WPS 自动化环境。如果运行环境没有这些组件，对应转换能力会不可用，但不会影响其他模块。
