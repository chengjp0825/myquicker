# 唤醒面板 UI 优化设计

> 设计日期：2026-07-04  
> 范围：MyQuicker 唤醒菜单（MainWindow）视觉与交互优化  
> 依据：docs/03-ui-and-styling.md §2 / §7

## 1. 背景与目标

当前唤醒面板采用 240×240 透明圆角窗口，内部为 72×72 深色方块矩阵 + 底部小齿轮。用户反馈“整体太死板 / 像方块矩阵”。

本次优化目标：
- 在保持紧凑矩阵布局的前提下提升精致感；
- 增加阴影、边框、悬停反馈和唤醒动画；
- 不破坏现有配置系统，老用户自定义值继续生效；
- 遵循 MainWindow “单例预热 + 屏幕外瞬移 + Opacity” 的极速唤醒规范（docs/03 §7）。

## 2. 设计决策

经过视觉方向、配色、设置入口、精致程度四轮的方案对比，最终确定：

- **布局方向**：紧凑网格优化版（保留矩阵，不改成大图标或启动台风格）。
- **配色方向**：暗色基调。
- **设置入口**：保留底部右侧齿轮，但优化为带 hover 的圆角按钮。
- **视觉增强**：外层投影 + 细边框 + 按钮悬停微缩放 + 唤醒淡入动画。
- **实施方案**：方案 B「精致紧凑网格」。

## 3. 视觉规范

### 3.1 面板（RootBorder）

| 属性 | 旧值 | 新值 | 说明 |
|------|------|------|------|
| 内容区尺寸 | 240×240 | 250×250 | 面板内容区；实际窗口为内容区 + 24 DIP（12 DIP 投影边距 ×2） |
| 背景色 | `#88000000` | `#E6202020` | 更浓郁、偏灰的半透明深底 |
| 圆角 | 16px | 16px | 保持不变 |
| 边框 | 无 | 1px `rgba(255,255,255,0.08)` | 让边缘在深色壁纸上更清晰 |
| 投影 | 无 | 底层 12 DIP 模糊黑色投影 | 见 §4.2 实现方式 |
| Margin | 无 | `12`（留给投影显示） | 内容面板整体内缩 12 DIP |
| Padding | 12 | `14,12,10,12`（左/上/右/下） | 底部略收，给底栏留空间 |

### 3.2 动作按钮（MenuButtonStyle）

| 属性 | 旧值 | 新值 |
|------|------|------|
| 尺寸 | 72×72 | 76×76 |
| Margin | 4 | 5 |
| Padding | 4 | 4 |
| 圆角 | 8px | 10px |
| 背景色（默认） | `#FF2D2D2D` | `#26FFFFFF`（半透明白） |
| 边框 | 无 | 1px `rgba(255,255,255,0.06)` |
| 图标字号 | 26px | 24px |
| 图标颜色 | 白色 90% 不透明 | 白色 `#FFFFFFFF` |
| 名称字号 | 12px | 11px |
| 名称颜色 | 白色 85% 不透明 | `#E0FFFFFF` |
| 名称 Margin | 0,2,0,2 | 0,2,0,1 |
| Hover 背景 | `#FF4A4A4A` | `#38FFFFFF` |
| Hover 缩放 | 无 | 1.05 |
| Pressed 背景 | 无 | `#20FFFFFF` |

### 3.3 底部设置入口

- 位置：仍放在面板底部右侧。
- 容器：底部分隔线 1px `rgba(255,255,255,0.08)`，与按钮矩阵间距 10px。
- 齿轮按钮：26×26 圆角按钮，圆角 6px，背景 `#20FFFFFF`，hover `#35FFFFFF`，图标 12px `#BBBBBB`。
- 不再使用 28×28 无边框按钮。

### 3.4 唤醒动画

- 触发：在 `WakeUp(POINT)` 中定位完成后播放。
- 效果：Opacity 0→1（150ms，EaseOutQuart）+ ScaleTransform 0.95→1（150ms，EaseOutBack）。
- 约束：动画期间不阻塞定位与 `SetWindowPos`；若用户连续触发，以当前状态为准，不堆叠动画。
- 隐藏：保持现有 `Sleep()` 的即时 `Opacity=0`，不加动画（避免用户点击后延迟消失）。

## 4. 实现要点

### 4.1 文件变更

- `Resources/ThemeStyles.xaml`
  - 重写 `MenuButtonStyle`，加入 `Border`、`RenderTransformOrigin`、`ScaleTransform`、hover/pressed 触发器。
  - 新增 `MenuSettingsButtonStyle`（底部齿轮专用样式）。
- `UI/MainWindow.xaml`
  - 调整 `RootBorder` 的 Padding、CornerRadius。
  - 在 `RootBorder` 外加投影层（§4.2）。
  - 调整 `ItemsControl` 中按钮尺寸、图标/文字字号。
  - 替换底部齿轮按钮样式。
- `UI/MainWindow.xaml.cs`
  - 在 `WakeUp` 中启动唤醒动画（需保证动画可安全重复调用）。
- `Models/SettingsModel.cs`
  - 更新 `MenuSettings` 默认值（§4.3）。
- `docs/03-ui-and-styling.md`
  - 更新 §2 中 MainWindow 视觉描述，与本次设计保持一致。

### 4.2 投影实现方式

WPF `AllowsTransparency=True` 窗口对 `DropShadowEffect` 支持不稳定，推荐采用“双层 Border”方案：

```xaml
<Grid>
    <!-- 阴影层：位于底层，填充整个窗口 -->
    <Border x:Name="ShadowBorder"
            Background="Black"
            Opacity="0.35"
            IsHitTestVisible="False">
        <Border.Effect>
            <BlurEffect Radius="12" />
        </Border.Effect>
    </Border>

    <!-- 内容层：真正的菜单面板，内缩 12 DIP 以露出底层投影 -->
    <Border x:Name="RootBorder" Margin="12" ... />
</Grid>
```

- 窗口实际尺寸 = 内容区尺寸 + 24 DIP（左右/上下各 12 DIP 投影边距），由 `ApplyMenuSettings` 计算。
- 阴影层不参与命中测试（`IsHitTestVisible="False"`），避免影响 `OnAnyMouseDown` 的外部点击检测。
- 阴影 `CornerRadius` 在 `ApplyMenuSettings` 中按 `menu.CornerRadius + 12` 设置，与面板圆角保持连续曲率。
- 阴影参数硬编码，不进入 `settings.json`。

### 4.3 配置与迁移

`MenuSettings` 默认值更新：

```csharp
public double Width { get; set; } = 250;
public double Height { get; set; } = 250;
public string Background { get; set; } = "#E6202020";
public int CornerRadius { get; set; } = 16;
public string ButtonBackground { get; set; } = "#26FFFFFF";
public string ButtonHoverBackground { get; set; } = "#38FFFFFF";
```

迁移策略：
- 老用户若修改过上述字段，`SettingsManager` 读取的 `settings.json` 值继续生效，不被覆盖。
- 老用户若使用默认值，删除或重置 `settings.json` 后首次启动会采用新默认值。
- 不引入配置版本号，也不强制覆盖旧值。

### 4.4 动画与性能

- 动画对象应使用 `Storyboard` 或 `DoubleAnimation`，在 `BeginAnimation` 前检查当前值，避免重复调用导致闪烁。
- 动画期间 `OnAnyMouseDown` 仍正常工作；若用户在动画播放时点击外部，直接 `Sleep()` 并将 Opacity 设为 0（可调用 `BeginAnimation(Opacity, null)` 清除动画）。
- 阴影层使用 `BlurEffect` 而非 `DropShadowEffect`，避免透明窗口渲染异常；`BlurRadius=18` 在 96/144 DPI 下表现稳定。

### 4.5 不变项

- 不改动 MainWindow 的生命周期、预热、Show/Hide 禁用、WS_EX_NOACTIVATE 等机制。
- 不改动唤醒零 IO 原则：`WakeUp` 路径不读取磁盘。
- 不改动 `ActionExecutor` / `ActionStore` 缓存机制。
- 不改动 `OnAnyMouseDown` 的外部点击睡眠逻辑。

## 5. 验收标准

- [ ] 唤醒面板默认尺寸 250×250，视觉符合 §3 规范。
- [ ] 面板有柔和投影和 1px 半透明边框。
- [ ] 动作按钮 76×76，hover 放大 1.05、按下背景变暗。
- [ ] 底部齿轮为圆角按钮，hover 有反馈。
- [ ] 唤醒时 150ms 淡入 + 微缩放，不卡顿。
- [ ] 外部点击、动作执行、打开设置后菜单立即隐藏。
- [ ] 老用户未自定义的字段采用新默认值；自定义字段保留原值。
- [ ] Release 构建下无异常，动画不影响极速唤醒体验。

## 6. 风险与回退

| 风险 | 影响 | 回退方案 |
|------|------|----------|
| `BlurEffect` 投影在某些显卡上性能差 | 低配置机器唤醒卡顿 | 改为纯透明 + 边框，去掉投影层 |
| 缩放动画与外部点击检测冲突 | 菜单无法正确隐藏 | 在 `Sleep()` 中清除所有进行中的动画 |
| 半透明按钮在极浅壁纸上对比度不足 | 可读性下降 | 提高默认按钮背景不透明度或加文字阴影 |
| 用户已习惯 72×72 小按钮 | 轻微布局变化 | 允许用户把 `MenuSettings.Width/Height` 改回 240 |

## 7. 后续工作

本设计通过后将进入实现计划阶段，由 `superpowers:writing-plans` 生成详细任务拆分与文件级改动清单。
