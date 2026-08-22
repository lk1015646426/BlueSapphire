# BlueSapphire Codex 工作规则

本文件与 `.agents/AGENTS.md` 同时生效；`.agents/AGENTS.md` 中的项目红线和测试要求优先保留。

## 任务范围

- 先读取 `README.md`、`DESIGN.md`、`DESIGN_LANGUAGE.md` 和 `.agents/AGENTS.md`。
- 新功能、架构调整和行为变化先输出目标、非目标、验收标准、影响文件和验证命令。
- Bug 修复、UI 优化、清理重构不得混在同一次任务中。
- 未经确认不得删除或重写 `Assets/DevMatrixLog.json`；新里程碑只能追加。
- 默认复用现有 WinUI 控件、主题资源和设计语言；不得为了“更漂亮”引入新的 UI 框架或替换现有导航壳。

## Bug 工作流

- 使用系统化调试：先复现、读取异常、检查最近改动、追踪状态/数据流，再提出单一根因假设。
- 根因确认前不得修改实现；修复前优先增加最小回归测试。
- 同一个假设失败两次后重新取证；三次失败后暂停补丁式修复，审查生命周期、并发、取消和状态边界。

## UI 工作流

- 使用 impeccable 做现状审计，使用 ui-ux-pro-max 做设计方向和组件建议，使用 winui-app 约束 WinUI 3/Fluent/主题/窗口行为。
- 先改信息层级、布局和可读性，再改颜色、动效和装饰；保持浅色/深色主题和响应式窗口行为。
- UI 任务不得改变业务逻辑；每次只处理一组可验证的视觉问题。

## 验证与交付

- 运行 `dotnet build --no-incremental` 和 `dotnet test BlueSapphire.Tests/BlueSapphire.Tests.csproj`；必要时再启动应用确认顶层窗口真实出现。
- 完成声明前检查 `git diff --check`、构建/测试退出码和警告数量，并列出未验证项。
- 不覆盖已有工作区改动，不执行破坏性 Git 命令，不执行 push。
