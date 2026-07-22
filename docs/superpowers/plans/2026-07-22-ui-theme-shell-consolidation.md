# UI Theme and Shell Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** 将剩余 WinUI 主题、应用外壳、设置、媒体、更新日志和对话框重构整理为依赖闭合、可独立构建和可视觉验证的提交。

**Architecture:** 先提交共享主题与运行时主题状态，再提交外壳/设置/粒子移除，然后提交主要页面和对话框，最后执行多尺寸、多主题截图与真实窗口验收。每个切片使用短路径临时工作树只应用 staged patch，运行 restore、全量测试和无增量构建。前三阶段已经提交的 Cleaner/AI 代码不重复改写。

**Tech Stack:** C#、.NET 8、WinUI 3、Windows App SDK、xUnit、PowerShell 7、Git worktree、System.Drawing 截图验证。

## Global Constraints

- `Assets/DevMatrixLog.json` 全阶段保持 SHA-256 `A9460CC2C03CFED65B36BE74E91D5CD2FBACD978D4243F8E6EFB9C81EBB64FEC`。
- 不执行 `git add .`、`git reset --hard`、`git clean` 或覆盖前三阶段提交。
- 不修改 Cleaner、AI、媒体业务算法和 API Key 存储语义。
- 不重新引入粒子背景、装饰性动效或第二套页面调色板。
- 每个 UI 候选提交必须在短路径隔离工作树完成 restore、全量测试和无增量构建。
- 全量测试不少于当前 218 项且零失败。
- Debug 与 Release 构建均为 0 警告、0 错误。
- 真实窗口需验证 1600×900、1280×800、窄窗口和大字号，并保留最终实例。

---

## File Map

### Verification tooling

- Create: `scripts/verify-ui-slice.ps1`

### Slice A — theme foundation

- Modify: `Themes/SharedTheme.xaml`
- Modify: `App.xaml.cs`
- Create: `Helpers/AcrylicHelper.cs`

### Slice B — shell, settings and particle removal

- Modify: `MainWindow.xaml`
- Modify: `MainWindow.xaml.cs`
- Modify: `SettingsPage.xaml`
- Modify: `SettingsPage.xaml.cs`
- Modify: `Models/AppMessages.cs`
- Delete: `Particle.cs`

### Slice C — primary pages

- Modify: `MediaManagerPage.xaml`
- Modify: `Views/DevLogPage.xaml`
- Modify: `Views/DevLogInputDialog.xaml`
- Modify: `Models/DevLogItem.cs`
- Modify: `Helpers/MarkdownHelper.cs`

### Slice D — dialogs

- Modify: `DuplicateResultDialog.xaml`
- Modify: `RenamePreviewDialog.xaml`
- Modify: `RenamePreviewDialog.xaml.cs`
- Modify: `Views/Dialogs/AdvancedImageEditorDialog.xaml`
- Modify: `Views/Dialogs/EnhanceImageDialog.xaml`
- Modify: `Views/Dialogs/EnhanceImageDialog.xaml.cs`
- Modify: `Views/Dialogs/FormatConvertDialog.xaml`
- Modify: `Views/Dialogs/FormatConvertDialog.xaml.cs`
- Modify: `Views/SkillProxyConfigDialog.xaml`

### Slice E — evidence

- Add: `UI_REDESIGN_SPEC.md`
- Add: `UI_REDESIGN_AUDIT.md`
- Add: `UI_REDESIGN_CHANGELOG.md`
- Add: `DESIGN.md`
- Add: `DESIGN_LANGUAGE.md`
- Modify: `README.md`
- Modify: this plan with actual results.

---

### Task 1: Add repeatable UI slice verification

**Files:**
- Create: `scripts/verify-ui-slice.ps1`

- [x] **Step 1: Create verifier from the proven AI verifier**

```powershell
Copy-Item scripts\verify-ai-slice.ps1 scripts\verify-ui-slice.ps1
```

Change the default filter to an empty string so every UI slice runs the full suite:

```powershell
[string]$TestFilter = ''
```

The script must retain short `C:\bsw` paths, staged-patch application, restore, tests, no-incremental build and cleanup failure detection.

- [x] **Step 2: Verify empty-index rejection and syntax**

```powershell
$caught = $null
try { .\scripts\verify-ui-slice.ps1 -Name empty-index } catch { $caught = $_.Exception.Message }
if ($caught -notmatch 'Git 暂存区为空') { throw '未拒绝空暂存区' }
$tokens = $null
$errors = $null
[Management.Automation.Language.Parser]::ParseFile(
 (Resolve-Path scripts\verify-ui-slice.ps1),
 [ref]$tokens,
 [ref]$errors) | Out-Null
if ($errors.Count -gt 0) { $errors; throw 'UI 验证脚本语法失败' }
```

- [x] **Step 3: Commit verifier**

```powershell
git add -- scripts/verify-ui-slice.ps1
git diff --cached --check
git commit -m "test: 添加 UI 切片隔离验证"
```

---

### Task 2: Consolidate the theme foundation

**Files:** Slice A.

**Interfaces:** Produces shared semantic resources, theme presets, font sizing and material fallback used by every page.

- [x] **Step 1: Verify theme resource uniqueness**

Run a Python check that extracts every `x:Key` from `Themes/SharedTheme.xaml`, fails on duplicate keys, and confirms these required keys exist:

```text
BgColor, SidebarBackground, PanelSurface, TextMain, TextSecondary,
AccentPrimary, AccentPrimaryHover, AccentPrimaryPressed, AccentPrimaryBg,
AccentSafe, AccentReview, AccentInspect, AccentDanger,
PrimaryActionButtonStyle, CompactButtonStyle, TextStyle_MetricLarge
```

- [x] **Step 2: Verify App theme presets**

Confirm `App.xaml.cs` contains exact preset branches for:

```text
default, sky, cobalt, graphite, lagoon, ink, ochre, sepia
```

and exact UI font size values:

```text
small, standard, medium, large
```

- [x] **Step 3: Stage Slice A**

```powershell
git add -- Themes/SharedTheme.xaml App.xaml.cs Helpers/AcrylicHelper.cs
git diff --cached --check
```

- [x] **Step 4: Isolate and commit**

```powershell
.\scripts\verify-ui-slice.ps1 -Name theme-foundation
git commit -m "feat: 完成全局主题与字号系统"
```

If XAML compilation proves `SettingsPage` or `MainWindow` directly references a renamed resource or method, merge only Slice B. Do not add page/dialog files merely for visual consistency.

---

### Task 3: Consolidate shell, settings and particle removal

**Files:** Slice B.

**Interfaces:** Consumes Slice A theme API; produces navigation shell, theme settings, reduced motion and particle-free lifecycle.

- [x] **Step 1: Verify particle removal candidate**

After staging, this source search must return no results:

```powershell
git grep --cached -n -E "Particle|ToggleParticleMessage|IsParticleEffectEnabled" -- '*.cs' '*.xaml'
```

- [x] **Step 2: Stage Slice B**

```powershell
git add -- MainWindow.xaml MainWindow.xaml.cs SettingsPage.xaml SettingsPage.xaml.cs Models/AppMessages.cs
git add -u -- Particle.cs
git diff --cached --check
```

- [x] **Step 3: Verify shell safety properties**

Confirm the staged source contains:

- normal background fallback when material setup fails;
- `MainWindow_Closed` event cleanup;
- minimum-size guard;
- no synchronous UI-thread wait;
- `ReduceMotion` save and immediate state update;
- theme preset and font size event handlers.

- [x] **Step 4: Isolate and commit**

```powershell
.\scripts\verify-ui-slice.ps1 -Name shell-settings
git commit -m "feat: 重建应用外壳与外观设置"
```

---

### Task 4: Consolidate Media and DevLog pages

**Files:** Slice C.

**Interfaces:** Produces themed, responsive page layouts without changing media or dev-log business behavior.

- [x] **Step 1: Run nearest service/ViewModel tests**

```powershell
dotnet test BlueSapphire.Tests\BlueSapphire.Tests.csproj --no-restore -v:minimal --filter "FullyQualifiedName~Media|FullyQualifiedName~DevLog|FullyQualifiedName~Markdown"
```

- [x] **Step 2: Verify page resource closure**

Extract every `StaticResource` / `ThemeResource` from the Slice C XAML files and confirm each key is defined in the page, `App.xaml` or committed `SharedTheme.xaml`.

- [x] **Step 3: Stage, isolate and commit**

```powershell
git add -- MediaManagerPage.xaml Views/DevLogPage.xaml Views/DevLogInputDialog.xaml Models/DevLogItem.cs Helpers/MarkdownHelper.cs
git diff --cached --check
.\scripts\verify-ui-slice.ps1 -Name primary-pages
git commit -m "feat: 统一媒体与更新日志界面"
```

---

### Task 5: Consolidate dialogs

**Files:** Slice D.

**Interfaces:** Produces one visual/interaction vocabulary for all transient workflows while preserving input/output behavior.

- [x] **Step 1: Run relevant media tests**

```powershell
dotnet test BlueSapphire.Tests\BlueSapphire.Tests.csproj --no-restore -v:minimal --filter "FullyQualifiedName~MediaRename|FullyQualifiedName~MediaDeduplication|FullyQualifiedName~Media"
```

- [x] **Step 2: Verify dialog accessibility**

For each icon-only button, require `AutomationProperties.Name` or visible text. Verify primary/cancel actions are present and dangerous actions include explanatory text.

- [x] **Step 3: Stage, isolate and commit**

```powershell
$dialogs = @(
 'DuplicateResultDialog.xaml',
 'RenamePreviewDialog.xaml',
 'RenamePreviewDialog.xaml.cs',
 'Views/Dialogs/AdvancedImageEditorDialog.xaml',
 'Views/Dialogs/EnhanceImageDialog.xaml',
 'Views/Dialogs/EnhanceImageDialog.xaml.cs',
 'Views/Dialogs/FormatConvertDialog.xaml',
 'Views/Dialogs/FormatConvertDialog.xaml.cs',
 'Views/SkillProxyConfigDialog.xaml'
)
git add -- $dialogs
git diff --cached --check
.\scripts\verify-ui-slice.ps1 -Name dialogs
git commit -m "feat: 统一工作台对话框体验"
```

---

### Task 6: Perform code and design review

- [x] **Step 1: Review resource ownership**

Confirm pages use shared semantic resources, no duplicate local palette, no missing theme keys and no circular aliases.

- [x] **Step 2: Review responsive behavior**

Confirm navigation and multi-column pages have explicit width behavior. Add RED source/XAML tests for concrete missing breakpoints or inaccessible controls before fixing them.

- [x] **Step 3: Review accessibility and motion**

Check keyboard focus, automation names, visible disabled states, non-color status cues and reduced-motion paths. Add focused regression tests for concrete defects.

- [x] **Step 4: Commit review fixes separately**

```powershell
git commit -m "fix: 加固 WinUI 响应式与可访问性"
```

Skip only when no production defect is found.

---

### Task 7: Capture visual evidence

**Outputs:** ignored files under `artifacts/ui-verification/`.

- [x] **Step 1: Build Debug and launch target pages**

Use `--tool=Home`, `--tool=MediaManager`, `--tool=CleanerAssistant` and normal Settings/DevLog navigation where supported.

- [x] **Step 2: Resize the real window**

Use Win32 `SetWindowPos` for:

```text
1600×900
1280×800
900×700 narrow
```

- [x] **Step 3: Capture screenshots**

Use `GetWindowRect` and `System.Drawing.Graphics.CopyFromScreen` to save PNGs for Home, Media, Cleaner, Settings and DevLog. Capture at least one light theme, one dark theme, one alternate preset and one large-font state.

- [x] **Step 4: Inspect screenshots**

Use visual inspection to verify:

- no clipped primary actions;
- no horizontal page overflow;
- readable hierarchy and contrast;
- navigation remains reachable;
- dialogs fit the window;
- reduced-motion state does not leave hidden/transparent content.

If a screenshot reveals a defect, reproduce it with exact size/theme settings, apply a focused fix, rebuild and recapture.

---

### Task 8: Update documentation and final verification

**Files:** Slice E.

- [x] **Step 1: Run full tests with history guard**

```powershell
$hashBefore = (Get-FileHash -Algorithm SHA256 Assets\DevMatrixLog.json).Hash
dotnet test BlueSapphire.Tests\BlueSapphire.Tests.csproj --no-restore -v:minimal
$hashAfter = (Get-FileHash -Algorithm SHA256 Assets\DevMatrixLog.json).Hash
if ($hashBefore -ne $hashAfter) { throw '测试修改了 DevMatrixLog.json' }
```

- [x] **Step 2: Verify clean committed HEAD independently**

Create `$finalPath = 'C:\bsw\ui_final_' + [Guid]::NewGuid().ToString('N').Substring(0, 8)` at `HEAD`, run restore, full test and Debug no-incremental build, then remove it normally.

- [x] **Step 3: Run current Debug and Release builds**

```powershell
dotnet build BlueSapphire.slnx --no-incremental --no-restore -v:minimal
dotnet build BlueSapphire.slnx -c Release --no-incremental --no-restore -v:minimal
```

Expected: both 0 warnings, 0 errors.

- [x] **Step 4: Update and commit UI documentation**

```powershell
git add -- UI_REDESIGN_SPEC.md UI_REDESIGN_AUDIT.md UI_REDESIGN_CHANGELOG.md DESIGN.md DESIGN_LANGUAGE.md README.md docs/superpowers/plans/2026-07-22-ui-theme-shell-consolidation.md
git diff --cached --check
git commit -m "docs: 记录全局 UI 重构验收结果"
```

Only mark sizes, themes and dialogs verified when screenshots or real-window checks exist.

- [x] **Step 5: Final process and boundary audit**

Leave one final verified x64 instance running. Then verify:

```powershell
git status --short --branch
git diff --cached --name-only
git grep -n -E "Particle|ToggleParticleMessage|IsParticleEffectEnabled" HEAD -- '*.cs' '*.xaml'
git ls-files Themes/SharedTheme.xaml Helpers/AcrylicHelper.cs UI_REDESIGN_SPEC.md UI_REDESIGN_AUDIT.md UI_REDESIGN_CHANGELOG.md
```

Expected:

- no staged files;
- no remaining product UI/theme modifications;
- no particle references;
- all UI design documents tracked;
- local collaboration configuration remains explicitly untracked or separately classified;
- final app has nonzero handle and `Responding=True`.


---

## 2026-07-22 Actual Results

- Slice verifier: `ce29251`; empty-index rejection and PowerShell syntax verified.
- Theme foundation: `533f410`, isolated 220/220 tests, Debug no-incremental build 0 warnings/0 errors.
- Shell/settings: `b310a03`, isolated 223/223 tests, particle source search empty, Debug no-incremental build 0 warnings/0 errors.
- Primary pages: `ba7ce98`, isolated 226/226 tests, resource closure test passed.
- Dialogs: `75a61d8`, isolated 229/229 tests, dialog resource/accessibility contract passed.
- Responsive/accessibility review: `e6806fb`, `fea2550`, `2e361a0`; final isolated review 233/233 tests and 0-warning build.
- Final current-worktree verification: 233/233 tests; x64 Debug and Release no-incremental builds both 0 warnings/0 errors.
- Visual evidence: 1600×900, 1280×800, 900×700; Dark/Light, Default/Sky/Ochre/Sepia, standard/large font and reduced-motion states.
- History guard: `Assets/DevMatrixLog.json` remained `A9460CC2C03CFED65B36BE74E91D5CD2FBACD978D4243F8E6EFB9C81EBB64FEC`.
