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
