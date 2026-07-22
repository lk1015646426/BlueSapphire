# BlueSapphire 旧版设计语言（历史归档）

> 本文件描述的是重构前的深蓝/青色视觉系统，已不再作为当前 UI 设计依据。当前规范请阅读 [UI_REDESIGN_SPEC.md](UI_REDESIGN_SPEC.md)，变更过程请阅读 [UI_REDESIGN_CHANGELOG.md](UI_REDESIGN_CHANGELOG.md)。

<!--

> 方向：**深蓝宝石暗色 · 单一青色信号 · 大量留白 · 发丝级边框 · 微妙深度辉光 · 技术性排版 · 克制而快的动效**
> 本规范建立在现有 `Themes/SharedTheme.xaml` 之上，所有新增令牌可直接并入该文件。

---

## 1. 设计哲学（一句话）

**安静的底，会发光的焦点。** 90% 界面保持中性深空色调，只用青色（`AccentCyan`）作为唯一"信号色"指路；科技感来自**深度、辉光、等宽数字、发丝线**，而不是堆装饰。

---

## 2. 现状评估（已达标，保留）

| 已有资产 | 评价 |
|---|---|
| `BgColor #0F1417` / `PanelSurface #151B1F` 深中性表面 | ✅ 优秀，保持 |
| `AccentCyan #26AFC7` 单一信号色 | ✅ 已符合"克制用色" |
| `CanvasControl` 背景 + HomePage 呼吸动画 | ✅ 科技感苗头，加强 |
| 8 倍数间距、`CornerRadius 10–12` | ✅ 保持 |
| `AccentFillColorDefaultBrush` 对齐原生控件 | ✅ 必须保留 |

---

## 3. 色彩系统（在现有令牌上扩展）

新增"辉光/渐变"令牌，强化科技感而不破坏现有色板：

```xml
<!-- 科技感扩展令牌（并入 SharedTheme.xaml） -->
<LinearGradientBrush x:Key="AccentGlowBrush" StartPoint="0,0" EndPoint="1,1">
    <GradientStop Offset="0"   Color="#26AFC7"/>
    <GradientStop Offset="1"   Color="#1B6E9E"/>
</LinearGradientBrush>

<!-- 主窗口背景的极淡径向辉光（青→透明），贴着 CanvasControl 画 -->
<RadialGradientBrush x:Key="AmbientGlow" Center="0.5,0.18" RadiusX="0.7" RadiusY="0.5">
    <GradientStop Offset="0"   Color="#1A2C36" Opacity="0.55"/>
    <GradientStop Offset="0.6" Color="#0F1417" Opacity="0"/>
</RadialGradientBrush>

<!-- 卡片顶部发丝高光（科技"边光"） -->
<LinearGradientBrush x:Key="EdgeSheen" StartPoint="0,0" EndPoint="0,1">
    <GradientStop Offset="0" Color="#3FFFFFFF" Opacity="0.10"/>
    <GradientStop Offset="0.04" Color="#00000000" Opacity="0"/>
</LinearGradientBrush>
```

**用色纪律**：青色只用于——主按钮、选中态、链接、关键状态点。其余一律中性灰阶。绝不在同一屏放第二个彩色。

---

## 4. 材质与深度（核心改动：从"平"到"微深"）

现有卡片全是纯 `SolidColorBrush` 平涂。科技极简靠**微妙深度**出味：

- **主内容区背景**：在 `MainWindow` 的 `BackgroundCanvas`（`OnDraw`）里画一层极淡的**点阵/网格纹理**（青色，透明度 < 0.04）+ 顶部 `AmbientGlow` 辉光。这是"科技感"最高性价比来源。
- **卡片**：保留 `PanelSurface`，叠加 `EdgeSheen` 顶部边光（1px 高光），边框降到 `BorderSubtle`（已 `#18FFFFFF`，很好）。
- **悬浮元素**（弹窗、tooltip、悬浮操作条）：用 `AcrylicBrush`（= `DesktopAcrylicBrush`）做真·磨砂玻璃，这是 WinUI 原生能力，零风险。

```xml
<AcrylicBrush x:Key="FloatingSurface"
    BackgroundSource="HostBackdrop"
    TintColor="#151B1F" TintOpacity="0.82"
    FallbackColor="#151B1F"/>
```

---

## 5. 排版（引入等宽 = 技术气质）

现有 `Segoe UI` 保持为正文。新增**等宽用于"数据/状态/代码"**，立刻有终端/技术工具的味道（很适合"工具箱"定位）：

```xml
<Style x:Key="TextStyle_Mono" TargetType="TextBlock"
       BasedOn="{StaticResource TextStyle_Body}">
    <Setter Property="FontFamily" Value="Cascadia Mono, Consolas"/>
    <Setter Property="FontFeatureSettings" Value="'tnum' 1"/> <!-- 等宽数字 -->
</Style>
```

- 指标数字（`MetricValueStyle`）：改用 `Cascadia Mono` + 等宽数字 → 数值对齐如仪表盘
- 状态/标签（chip、badge）：等宽小字 → 技术感
- 标题/正文：维持 `Segoe UI`，**不要**全用等宽（会显廉价）

---

## 6. 间距与栅格（已规范，收紧一档）

现有 8 倍数系统保留。极简感来自**更大的留白**：

| 令牌 | 当前 | 建议 |
|---|---|---|
| `PagePadding` | `24,20` | `40,32`（首页/设置页更空） |
| `SectionGap` | `0,32,0,0` | `0,40,0,0` |
| 卡片内距 | `18,16` | `20,18` |

栅格：内容列 `MaxWidth` 维持（HomePage 已 `720`），所有页面统一居中单列，避免内容铺满产生"拥挤感"。

---

## 7. 动效语言（定义标准曲线，统一科技感）

现有 HomePage 动画很好（呼吸 + 入场）。需**固化一套全应用统一的动效令牌**，避免各处手感不一：

| 场景 | 时长 | 曲线 | 幅度 |
|---|---|---|---|
| 页面/卡片入场 | 280ms | `CubicEase` Out | 位移 16px + 淡入 |
| 悬停反馈 | 120ms | `SineEase` | 仅边框/亮度变化 |
| 选中/激活 | 160ms | `SineEase` | 青色填充 |
| 弹窗出现 | 200ms | `BackEase` Out（轻微过冲） | 缩放 0.96→1 |

**纪律**：动效只做"微"——位移 ≤ 20px、缩放 ≤ 1.04、永远可打断。绝不用弹跳/旋转等花哨运动（那是"玩具感"，不是科技感）。

---

## 8. 组件视觉变体（科技极简版）

**主按钮**：保留青色填充，但**去掉 1px 同色边框**，悬停时改用 `AccentGlowBrush` 渐变 + 极淡外发光（`DropShadow` 青色，blur 12，opacity 0.25）。

**卡片**：加 `EdgeSheen` 顶部边光；非激活态完全无边框也可（靠表面色差分区），激活/选中才显 `BorderActive`。

**分隔**：用 `BorderSubtle` 发丝线（`1px`，opacity 极低）替代大色块分区 → 更透气。

**输入框**：底色 `PanelSurfaceStrong`，聚焦时边框变 `AccentCyan` + 同色 1px 内发光，无阴影。

---

## 9. "科技感"来源清单（按性价比排序）

1. **背景点阵/网格纹理**（CanvasControl 画）→ 最高性价比，一眼"技术"
2. **等宽数字/状态字** → 低成本，强气质
3. **青色辉光（按钮 hover / 选中态）** → 信号感
4. **磨砂玻璃弹窗** → 高级感
5. **顶部环境辉光** → 空间感
6. 入场微动效统一 → 整体"顺"

---

## 10. 反模式（绝不做，否则变"中规中矩"或"杀马特"）

- ❌ 第二个彩色（红绿紫蓝齐飞）→ 失去信号聚焦
- ❌ 大圆角（>16）或全圆胶囊 → 变"可爱风"，非科技
- ❌ 重投影/厚阴影 → 变"拟物"，非极简
- ❌ 渐变铺满背景 → 变"赛博朋克"，非极简
- ❌ 花哨动效（旋转、弹跳、粒子喷发）→ 变"玩具"
- ❌ 全屏等宽字体 → 廉价

---

## 11. 落地优先级（建议顺序）

1. **背景纹理 + 顶部辉光**（CanvasControl `OnDraw`）—— 改一处，全局科技感
2. **等宽字体令牌** + 指标/状态字替换 —— 改令牌，批量生效
3. **动效令牌固化** —— 统一手感
4. **按钮/卡片辉光变体** —— 点睛
5. 留白收紧、磨砂弹窗 —— 收尾
-->

> 所有改动以"不破坏现有 `SharedTheme.xaml` 令牌命名"为前提，新增而非覆盖，旧页面零回归。
