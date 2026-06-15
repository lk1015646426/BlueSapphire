# BlueSapphire

BlueSapphire 是一个面向 Windows 11 的 WinUI 3 工具箱，当前已经包含两条核心产品线：

- `媒体管家`：图片的扫描、整理、去重与批处理
- `清理助手`：空间分析、风险分层、恢复、排除、提权清理、规则热更新

项目目标不是做“堆功能的工具集合”，而是做一套风格统一、交互清晰、可长期演进的桌面工具平台。

## 版本信息

- 当前文档版本：`1.0.7`
- 当前发布形态：`Windows 11 x64` 安装包
- 当前发布链路：`dotnet publish -> Inno Setup -> GitHub Releases`

### 1.0.7 更新摘要

- 消除非事件处理器中的 `async void`，避免静默崩溃风险
- 拆解 `CleanerAssistantViewModel` 至独立功能模块（扫描、清理、自动化等），大幅提升可维护性
- 补齐 ViewModel 层与 Media 模块核心业务逻辑的自动化测试用例
- 采用 `DPAPI` 加密方式实现 `DeepSeek API Key` 安全存储机制
- 引入 `IHttpClientFactory` 优化 HTTP 请求性能及生命周期管理
- 统一日志抽象至 `Microsoft.Extensions.Logging`，全盘接管启动和清理相关的输出

## 当前模块

- `主页`：工具导航与整体状态入口
- `媒体管家`：图片扫描、去重、时间重命名、格式转换、尺寸调整、裁剪、压缩、增强、自定义标签
- `清理助手`：快速扫描、深度扫描、空间分析、隔离恢复、自动低风险保洁、规则治理
- `设置`：应用行为与视觉项配置
- `开发日志`：内部调试与版本记录页

## 技术栈

- `.NET 8`
- `WinUI 3 / Windows App SDK`
- `CommunityToolkit.Mvvm`

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
├── Services/                   # 扫描、清理、图片处理等服务
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

当前仓库已经预留正式截图目录：

- `docs/screenshots/home.png`
- `docs/screenshots/media-manager.png`
- `docs/screenshots/cleaner-assistant.png`

代表界面如下：

### 首页

![BlueSapphire Home](docs/screenshots/home.png)

### 媒体管家

![BlueSapphire Media Manager](docs/screenshots/media-manager.png)

### 清理助手

![BlueSapphire Cleaner Assistant](docs/screenshots/cleaner-assistant.png)

补充说明：

- `Assets/StoreLogo.png` 继续作为仓库 Logo 资源保留

## 安装包下载说明

正式安装包统一通过 GitHub Releases 分发：

- 下载地址：[BlueSapphire Releases](https://github.com/lk1015646426/BlueSapphire/releases)
- 建议发布标签：`v1.0.7`

下载建议：

- 普通用户直接下载 `BlueSapphire_Setup_v1.0.7.exe` 或更新版本
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

## 已知限制

- README 截图目录已经预留，但最终版界面截图仍需后续替换进 `docs/screenshots/`
- 系统级清理涉及权限边界，部分路径必须在管理员模式下才可执行

## 后续路线

- 持续扩充清理规则库与发布质量治理
- 继续完善媒体处理链路的真实场景验证
- 持续收口安装包、Release 和 Builder 的发版体验

## 开发日志与版本记录

> **[修复优化] 修复 AI 智能助手连续触发指令时的陷入死循环的 Bug，并优化提示词让 AI 执行清理任务更加灵活**
> 
> **1.0.7** | 2026-06-15 23:11:56
> 
> `release(1.0.7): 修复 DeepSeek 原生 tool_calls JSON 结构解析导致的死循环 Bug；修改 AI 强制提示词（Prompt），赋予智能免询问立即扫描能力；完善多轮 Tool Call History 的状态保持。`
