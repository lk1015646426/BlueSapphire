<div align="center">
  <img src="Assets/AppIcon.png" width="144" alt="BlueSapphire 蓝宝石工具箱图标">
  <h1>BlueSapphire 蓝宝石工具箱</h1>
  <p>面向 Windows 10/11 的现代化智能桌面工具箱</p>
  <p><strong>WinUI 3 · .NET 8 · x64 · 当前版本 1.0.3</strong></p>
</div>

BlueSapphire 是一个面向 Windows 10/11 的 WinUI 3 工具箱，当前包含四条核心产品线：

- `AI Copilot`：基于大语言模型的智能助理，支持对话、工具导航，以及需要用户确认的操作调用
- `媒体管家`：基于 dHash 的相似图片候选扫描、SHA-256 精确去重、标签与图片批处理
- `清理助手`：空间分析、风险分层、隔离恢复、自动低风险保洁、安全隔离区与规则治理
- `更新日志`：面向用户的版本记录展示，以及开发环境中的受控编辑

项目目标不是做“堆功能的工具集合”，而是做一套风格统一、交互清晰、可长期演进的现代化智能桌面平台。

## 版本信息

- 当前文档版本：`1.0.3`
- 当前发布形态：`Windows 10 1809+ / Windows 11 x64` 安装包
- 当前发布链路：`dotnet publish -> Inno Setup -> GitHub Releases`

### 1.0.3 核心更新

- **统一品牌视觉**：应用、安装程序、快捷方式、首页和关于页统一使用新版 `BS.ico`。
- **清理安全闭环**：完善快速/深度扫描、风险分层、隔离恢复、失败重试、取消和自动低风险保洁。
- **媒体能力升级**：精确去重采用 SHA-256，相似图片检测采用 dHash，并补齐标签、批处理和结果反馈。
- **AI 数据保护**：API Key、对话历史与长期记忆使用当前 Windows 账户加密保存，危险工具调用必须确认。
- **扩展默认不信任**：MCP、Web Skill 与 Agent Skill 增加审核、命令白名单、SSRF 防护和响应大小限制。
- **现代化主题体验**：支持系统、亮色、暗色、高对比度和减少动态效果，补齐键盘与屏幕阅读器体验。
- **工程发布加固**：新增 CI、154 项自动化测试、发布资源门禁、可选代码签名和 SHA-256 校验文件。

## 当前模块

- `主页`：工具导航与整体状态入口
- `AI 智能助理`：支持自然语言多轮对话，内置 Agent 引擎，可根据指令直达界面或调用清理任务、生成日志
- `媒体管家`：图片智能扫描、精准相似度去重、文件无感动态刷新
- `清理助手`：快速/深度扫描、磁盘空间分析、隔离恢复与系统级权限保洁
- `设置`：应用行为与视觉项配置、大模型 API Key 加密存储管理
- `更新日志`：版本记录自动合并，发布环境只读、开发环境可编辑
- `关于`：展示版本、运行环境与项目架构信息

## 技术栈

- `.NET 8`
- `WinUI 3 / Windows App SDK`
- `CommunityToolkit.Mvvm`

运行目标：

- `Windows 10 1809+ / Windows 11 x64`
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

- [`installer.iss`](installer.iss) 是 `publish-only`
- 必须通过 `/dSourcePath=<publish目录>` 显式传入发布目录
- 不允许直接把仓库根目录传给 `installer.iss`
- 如果 `SourcePath` 包含 `BlueSapphire.Tests`、`TestData`、`.git`、`obj`、`bin\Debug` 等内容，编译会直接失败

### 本地打包

```powershell
ISCC.exe `
  /dSourcePath="C:\path\to\publish" `
  /dMyAppName="BlueSapphire" `
  /dMyAppVersion="1.0.3" `
  /dMyAppPublisher="BlueSapphire Team" `
  /dMyAppId="{{8D43FBFA-A424-4FED-BDE6-6C586D7D13EE}" `
  installer.iss
```

### GitHub Release

仓库已内置发布工作流：

- [`.github/workflows/release.yml`](.github/workflows/release.yml)

触发方式：

- 推送 `v*` tag
- 手动触发 `workflow_dispatch`

工作流会自动：

- 安装 `.NET 8 SDK`
- 安装 `Inno Setup 6`
- 执行 `dotnet publish`
- 执行完整测试并校验发布输出
- 用 `installer.iss` 生成安装包
- 生成 SHA-256 校验文件；配置签名证书时自动签名安装包
- 将安装包上传到 GitHub Release

## 项目结构

```text
BlueSapphire/
├── .github/workflows/          # GitHub Actions
├── Assets/                     # 运行时资源、规则、图标、初始日志配置
├── BlueSapphire.Tests/         # 单元测试与回归测试
├── Controls/                   # 可复用 WinUI 控件
├── Helpers/                    # 配置与通用辅助
├── Interfaces/                 # UI/服务交互接口
├── Models/                     # 业务模型
├── Services/                   # 扫描、清理、图片处理及大模型调度服务
├── Tools/                      # AI Agent 工具与导航工具定义
├── ViewModels/                 # MVVM 视图模型
├── Views/                      # 页面与对话框
├── TestData/                   # 测试样本
├── Themes/                     # 主题画刷与共用样式
├── installer.iss               # publish-only 安装脚本
└── BlueSapphire.csproj         # 主项目
```

## 说明

- `Assets\CleanerRules.json`、`Assets\DevMatrixLog.json` 与根目录 `BS.ico` 属于正式运行时资源，会跟随发布
- `BlueSapphire.Tests` 和 `TestData` 仅用于开发与验证，不进入正式发布目录
- 如果你想一键构建安装包，配套工具仓库是 `BlueSapphire-Builder`

## 配套仓库

- `BlueSapphire-Builder`：本地一键发布工具，负责执行 `dotnet publish -> ISCC -> 安装包`

## 截图展示

当前仓库包含以下界面截图：

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

- 应用程序、快捷方式、首页与关于页统一使用根目录 `BS.ico`
- GitHub README 使用 `Assets/AppIcon.png` 展示同款 PNG 预览图

## 安装包下载说明

正式安装包统一通过 GitHub Releases 分发：

- 下载地址：[BlueSapphire Releases](https://github.com/lk1015646426/BlueSapphire/releases)
- 当前发布标签：`v1.0.3`

下载建议：

- 普通用户直接下载 `BlueSapphire_Setup_v1.0.3.exe` 或更新版本
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

- 系统级清理涉及权限边界，部分路径必须在管理员模式下才可执行
- AI Copilot 需要用户自行配置受支持供应商的 API Key；密钥使用当前 Windows 账户加密保存
- MCP、远程 OpenAPI 与 Agent 技能属于第三方扩展，默认停用或待审核，启用和实际调用均保留确认边界

## 后续路线

- 持续扩充清理规则库与发布质量治理
- 继续完善媒体处理链路的真实场景验证
- 拓展 AI Copilot 的更多桌面级别 Agent 能力
- 持续收口安装包、Release 和 Builder 的发版体验

## 开发日志与版本记录

详见客户端内嵌的 **开发日志** 模块，AI 智能助理可根据指令自动为您提取与生成最新开发记录！
