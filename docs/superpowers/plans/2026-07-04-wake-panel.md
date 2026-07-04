# 唤醒面板 UI 优化实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不改变 MainWindow 生命周期与极速唤醒机制的前提下，将唤醒菜单从“深色方块矩阵”升级为带阴影、边框、悬停缩放和淡入动画的精致紧凑网格。

**Architecture：** 视觉层全部通过 XAML 样式与少量 code-behind 动画实现；配置默认值更新在 `MenuSettings`，老用户自定义值由 `SettingsManager` 自然保留；投影采用双层 Border（BlurEffect）避免 `AllowsTransparency` 窗口对 `DropShadowEffect` 的不稳定支持。

**Tech Stack：** .NET 8 WPF，XAML，C#，P/Invoke（`SetWindowPos`、`WS_EX_NOACTIVATE` 已存在，不改动）。

---

## 文件结构

| 文件 | 职责 | 变更类型 |
|------|------|----------|
| `Models/SettingsModel.cs` | `MenuSettings` 默认值 | 修改 |
| `Resources/ThemeStyles.xaml` | `MenuButtonStyle` 重写 + 新增 `MenuSettingsButtonStyle` | 修改 |
| `UI/MainWindow.xaml` | 面板阴影层、Padding、按钮尺寸、图标/文字字号、底部齿轮 | 修改 |
| `UI/MainWindow.xaml.cs` | `WakeUp` 方法加入淡入/缩放动画，并在 `Sleep()` 中安全清除动画 | 修改 |
| `docs/03-ui-and-styling.md` | 更新 MainWindow 视觉描述 | 修改 |

---

### Task 1：更新 MenuSettings 默认值

**Files:**
- Modify: `Models/SettingsModel.cs:110-125`

- [ ] **Step 1：修改默认值**

  将 `MenuSettings` 类中的默认值改为新设计值：

  ```csharp
  /// <summary>菜单窗口宽度（DIP）。</summary>
  public double Width { get; set; } = 250;

  /// <summary>菜单窗口高度（DIP）。</summary>
  public double Height { get; set; } = 250;

  /// <summary>菜单外层半透明背景色（ARGB 十六进制）。</summary>
  public string Background { get; set; } = "#E6202020";

  /// <summary>菜单外层圆角半径（px）。</summary>
  public int CornerRadius { get; set; } = 16;

  /// <summary>动作按钮背景色。</summary>
  public string ButtonBackground { get; set; } = "#26FFFFFF";

  /// <summary>动作按钮悬停背景色。</summary>
  public string ButtonHoverBackground { get; set; } = "#38FFFFFF";
  ```

- [ ] **Step 2：编译检查**

  Run: `dotnet build`
  Expected: 编译成功，无错误。

- [ ] **Step 3：Commit**

  ```bash
  git add Models/SettingsModel.cs
  git commit -m "feat(menu): 更新 MenuSettings 默认视觉值"
  ```

---

### Task 2：重写 MenuButtonStyle

**Files:**
- Modify: `Resources/ThemeStyles.xaml:36-62`

- [ ] **Step 1：替换 MenuButtonStyle**

  将现有 `MenuButtonStyle` 替换为以下样式（保持 `DynamicResource` 让背景色仍可从 code-behind 注入）：

  ```xaml
  <!-- 唤醒菜单动作按钮。背景色走 DynamicResource，由 MainWindow code-behind 从 JSON 注入 -->
  <Style x:Key="MenuButtonStyle" TargetType="Button">
      <Setter Property="Foreground" Value="White" />
      <Setter Property="Background" Value="{DynamicResource MenuButtonBackgroundBrush}" />
      <Setter Property="BorderThickness" Value="1" />
      <Setter Property="BorderBrush" Value="#15FFFFFF" />
      <Setter Property="FontSize" Value="{StaticResource MenuButtonFontSize}" />
      <Setter Property="Padding" Value="{StaticResource MenuButtonPadding}" />
      <Setter Property="Cursor" Value="Hand" />
      <Setter Property="RenderTransformOrigin" Value="0.5,0.5" />
      <Setter Property="RenderTransform">
          <Setter.Value>
              <ScaleTransform ScaleX="1" ScaleY="1" />
          </Setter.Value>
      </Setter>
      <Setter Property="Template">
          <Setter.Value>
              <ControlTemplate TargetType="Button">
                  <Border x:Name="Bd"
                          Background="{TemplateBinding Background}"
                          BorderBrush="{TemplateBinding BorderBrush}"
                          BorderThickness="{TemplateBinding BorderThickness}"
                          CornerRadius="{StaticResource MenuButtonCornerRadius}"
                          Padding="{TemplateBinding Padding}"
                          SnapsToDevicePixels="True">
                      <ContentPresenter HorizontalAlignment="Center"
                                        VerticalAlignment="Center" />
                  </Border>
                  <ControlTemplate.Triggers>
                      <Trigger Property="IsMouseOver" Value="True">
                          <Setter TargetName="Bd" Property="Background"
                                  Value="{DynamicResource MenuButtonHoverBackgroundBrush}" />
                          <Setter TargetName="Bd" Property="BorderBrush" Value="#25FFFFFF" />
                      </Trigger>
                      <Trigger Property="IsPressed" Value="True">
                          <Setter TargetName="Bd" Property="Background" Value="#20FFFFFF" />
                      </Trigger>
                  </ControlTemplate.Triggers>
              </ControlTemplate>
          </Setter.Value>
      </Setter>
      <Style.Triggers>
          <Trigger Property="IsMouseOver" Value="True">
              <Trigger.EnterActions>
                  <BeginStoryboard>
                      <Storyboard>
                          <DoubleAnimation Storyboard.TargetProperty="RenderTransform.ScaleX"
                                            To="1.05" Duration="0:0:0.12" />
                          <DoubleAnimation Storyboard.TargetProperty="RenderTransform.ScaleY"
                                            To="1.05" Duration="0:0:0.12" />
                      </Storyboard>
                  </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                  <BeginStoryboard>
                      <Storyboard>
                          <DoubleAnimation Storyboard.TargetProperty="RenderTransform.ScaleX"
                                            To="1" Duration="0:0:0.12" />
                          <DoubleAnimation Storyboard.TargetProperty="RenderTransform.ScaleY"
                                            To="1" Duration="0:0:0.12" />
                      </Storyboard>
                  </BeginStoryboard>
              </Trigger.ExitActions>
          </Trigger>
      </Style.Triggers>
  </Style>
  ```

- [ ] **Step 2：更新相关尺寸资源**

  在同文件中，将 `MenuButtonCornerRadius` 从 `8` 改为 `10`：

  ```xaml
  <CornerRadius x:Key="MenuButtonCornerRadius">10</CornerRadius>
  ```

- [ ] **Step 3：编译检查**

  Run: `dotnet build`
  Expected: 编译成功。

- [ ] **Step 4：Commit**

  ```bash
  git add Resources/ThemeStyles.xaml
  git commit -m "feat(menu): 重写 MenuButtonStyle，加边框与悬停缩放"
  ```

---

### Task 3：新增 MenuSettingsButtonStyle

**Files:**
- Modify: `Resources/ThemeStyles.xaml`（在 `MenuButtonStyle` 之后插入）

- [ ] **Step 1：在 ThemeStyles.xaml 中添加齿轮样式**

  在 `MenuButtonStyle` 闭合标签 `</Style>` 之后新增：

  ```xaml
  <!-- 唤醒菜单底部设置按钮。比动作按钮更紧凑，但保持同语言 -->
  <Style x:Key="MenuSettingsButtonStyle" TargetType="Button">
      <Setter Property="Background" Value="#20FFFFFF" />
      <Setter Property="BorderBrush" Value="#15FFFFFF" />
      <Setter Property="BorderThickness" Value="1" />
      <Setter Property="Foreground" Value="#BBBBBB" />
      <Setter Property="Cursor" Value="Hand" />
      <Setter Property="Width" Value="26" />
      <Setter Property="Height" Value="26" />
      <Setter Property="Padding" Value="0" />
      <Setter Property="Template">
          <Setter.Value>
              <ControlTemplate TargetType="Button">
                  <Border x:Name="Bd"
                          Background="{TemplateBinding Background}"
                          BorderBrush="{TemplateBinding BorderBrush}"
                          BorderThickness="{TemplateBinding BorderThickness}"
                          CornerRadius="6"
                          SnapsToDevicePixels="True">
                      <ContentPresenter HorizontalAlignment="Center"
                                        VerticalAlignment="Center" />
                  </Border>
                  <ControlTemplate.Triggers>
                      <Trigger Property="IsMouseOver" Value="True">
                          <Setter TargetName="Bd" Property="Background" Value="#35FFFFFF" />
                          <Setter TargetName="Bd" Property="BorderBrush" Value="#22FFFFFF" />
                      </Trigger>
                      <Trigger Property="IsPressed" Value="True">
                          <Setter TargetName="Bd" Property="Background" Value="#18FFFFFF" />
                      </Trigger>
                  </ControlTemplate.Triggers>
              </ControlTemplate>
          </Setter.Value>
      </Setter>
  </Style>
  ```

- [ ] **Step 2：编译检查**

  Run: `dotnet build`
  Expected: 编译成功。

- [ ] **Step 3：Commit**

  ```bash
  git add Resources/ThemeStyles.xaml
  git commit -m "feat(menu): 添加底部设置按钮样式"
  ```

---

### Task 4：重构 MainWindow.xaml 面板与按钮矩阵

**Files:**
- Modify: `UI/MainWindow.xaml`

- [ ] **Step 1：用 Grid 包裹 RootBorder 并加入阴影层**

  将根 `Border` 替换为 `Grid`，内部加入阴影层和原 `RootBorder`。注意：阴影层填充整个窗口，内容面板通过 `Margin="12"` 内缩以露出底层投影：

  ```xaml
  <Window ...>
      <Grid>
          <!-- 阴影层：填充整个窗口，不参与命中测试 -->
          <Border x:Name="ShadowBorder"
                  Background="Black"
                  Opacity="0.35"
                  IsHitTestVisible="False">
              <Border.Effect>
                  <BlurEffect Radius="12" />
              </Border.Effect>
          </Border>

          <!-- 12px 边距留给底层投影；右下 padding 略收给底栏/滚动条留余量 -->
          <Border x:Name="RootBorder" Margin="12" Padding="14,12,10,12">
              ...（原有 Grid 内容保持不变）...
          </Border>
      </Grid>
  </Window>
  ```

- [ ] **Step 2：调整按钮矩阵尺寸与字号**

  在 `ItemsControl.ItemTemplate` 中的 `Button` 上：

  ```xaml
  <Button Click="ActionButton_Click"
          Style="{StaticResource MenuButtonStyle}"
          Width="76" Height="76"
          Margin="5"
          Padding="4"
          Cursor="Hand">
  ```

  在按钮内部的 `TextBlock` 中：

  ```xaml
  <!-- 图标 -->
  <TextBlock Grid.Row="0"
             FontFamily="Segoe MDL2 Assets"
             Text="{Binding Icon, Converter={StaticResource HexToGlyphConverter}}"
             FontSize="24"
             HorizontalAlignment="Center"
             VerticalAlignment="Center"/>

  <!-- 文字 -->
  <TextBlock Grid.Row="1"
             Text="{Binding Name}"
             FontSize="11"
             Foreground="#E0FFFFFF"
             TextAlignment="Center"
             TextTrimming="CharacterEllipsis"
             Margin="0,2,0,1" />
  ```

- [ ] **Step 3：更新 ApplyMenuSettings 以容纳投影边距**

  文件：`UI/MainWindow.xaml.cs` 中的 `ApplyMenuSettings` 方法。

  将窗口尺寸设置为内容区 + 24 DIP，并同步阴影圆角：

  ```csharp
  Width = menu.Width + 24;
  Height = menu.Height + 24;
  RootBorder.Background = BrushHelper.ToBrush(menu.Background);
  RootBorder.CornerRadius = new CornerRadius(menu.CornerRadius);
  ShadowBorder.CornerRadius = new CornerRadius(menu.CornerRadius + 12);
  Resources["MenuButtonBackgroundBrush"] = BrushHelper.ToBrush(menu.ButtonBackground);
  Resources["MenuButtonHoverBackgroundBrush"] = BrushHelper.ToBrush(menu.ButtonHoverBackground);
  ```

- [ ] **Step 4：更新 OnAnyMouseDown 判定区域为内容区**

  文件：`UI/MainWindow.xaml.cs` 中的 `OnAnyMouseDown` 方法。

  替换原来的全窗口边界判断为内容区边界判断，避免点击投影边距时菜单不消失：

  ```csharp
  var contentBounds = RootBorder.TransformToAncestor(this)
      .TransformBounds(new Rect(0, 0, RootBorder.ActualWidth, RootBorder.ActualHeight));
  contentBounds.Offset(Left, Top);
  if (!contentBounds.Contains(p))
      Sleep();
  ```

- [ ] **Step 5：更新设置页标签**

  文件：`UI/SettingsWindow.xaml` 中「菜单」设置页的「窗口宽度/高度」标签。

  由于 `MenuSettings.Width/Height` 现在表示面板内容区尺寸，而非实际窗口尺寸，修改标签文案：
  - "窗口宽度" → "面板宽度"
  - "单位 DIP" → "内容区宽度，单位 DIP"
  - "窗口高度" → "面板高度"
  - "单位 DIP" → "内容区高度，单位 DIP"

- [ ] **Step 6：编译检查**

  Run: `dotnet build`
  Expected: 编译成功。

- [ ] **Step 7：Commit**

  ```bash
  git add UI/MainWindow.xaml UI/MainWindow.xaml.cs UI/SettingsWindow.xaml
  git commit -m "feat(menu): 面板加阴影层，按钮改为 76x76 并优化字号"
  ```

---

### Task 5：优化底部设置入口

**Files:**
- Modify: `UI/MainWindow.xaml:71-83`

- [ ] **Step 1：替换底部工具区 XAML**

  将底部 `Border` 与 `Button` 替换为：

  ```xaml
  <!-- 2. 底部工具区 -->
  <Border Grid.Row="1" Margin="5,10,5,0" Padding="0,8,0,0"
          BorderBrush="#18FFFFFF" BorderThickness="0,1,0,0">
      <Button x:Name="SettingsButton"
              Click="SettingsButton_Click"
              Style="{StaticResource MenuSettingsButtonStyle}"
              HorizontalAlignment="Right"
              ToolTip="设置">
          <TextBlock FontFamily="Segoe MDL2 Assets" Text="&#xE713;" FontSize="12" />
      </Button>
  </Border>
  ```

- [ ] **Step 2：编译检查**

  Run: `dotnet build`
  Expected: 编译成功。

- [ ] **Step 3：Commit**

  ```bash
  git add UI/MainWindow.xaml
  git commit -m "feat(menu): 底部齿轮改为圆角按钮样式"
  ```

---

### Task 6：添加唤醒淡入与缩放动画

**Files:**
- Modify: `UI/MainWindow.xaml.cs`

- [ ] **Step 1：在 MainWindow 构造函数中初始化 ScaleTransform**

  在 `InitializeComponent();` 之后添加：

  ```csharp
  // 为唤醒动画准备缩放变换（默认 1,1）
  RootBorder.RenderTransformOrigin = new Point(0.5, 0.5);
  RootBorder.RenderTransform = new ScaleTransform(1, 1);
  ```

- [ ] **Step 2：修改 WakeUp 方法播放动画**

  将现有 `WakeUp` 方法：

  ```csharp
  private void WakeUp(POINT e)
  {
      PositionAtCursor(e);
      Opacity = 1;
      _isAwake = true;
      ...
  }
  ```

  替换为：

  ```csharp
  private void WakeUp(POINT e)
  {
      PositionAtCursor(e);

      // 清除可能残留的动画，确保每次从初始状态开始
      BeginAnimation(OpacityProperty, null);
      var scale = (ScaleTransform)RootBorder.RenderTransform;
      scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
      scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

      scale.ScaleX = 0.95;
      scale.ScaleY = 0.95;
      Opacity = 0;
      _isAwake = true;

      var storyboard = new Storyboard();

      var opacityAnimation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
      {
          EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
      };
      Storyboard.SetTarget(opacityAnimation, this);
      Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(OpacityProperty));
      storyboard.Children.Add(opacityAnimation);

      var scaleXAnimation = new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(150))
      {
          EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
      };
      Storyboard.SetTarget(scaleXAnimation, RootBorder);
      Storyboard.SetTargetProperty(scaleXAnimation, new PropertyPath("RenderTransform.ScaleX"));
      storyboard.Children.Add(scaleXAnimation);

      var scaleYAnimation = new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(150))
      {
          EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
      };
      Storyboard.SetTarget(scaleYAnimation, RootBorder);
      Storyboard.SetTargetProperty(scaleYAnimation, new PropertyPath("RenderTransform.ScaleY"));
      storyboard.Children.Add(scaleYAnimation);

      storyboard.Begin();

      var hwnd = new WindowInteropHelper(this).Handle;
      NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
          NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
  }
  ```

- [ ] **Step 3：修改 Sleep 方法清除动画**

  在 `Sleep()` 方法中加入动画清除，避免隐藏时动画仍在播放：

  ```csharp
  internal void Sleep()
  {
      BeginAnimation(OpacityProperty, null);
      var scale = (ScaleTransform)RootBorder.RenderTransform;
      scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
      scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

      Opacity = 0;
      Left = -9999;
      Top = -9999;
      _isAwake = false;
  }
  ```

- [ ] **Step 4：编译检查**

  Run: `dotnet build`
  Expected: 编译成功。

- [ ] **Step 5：Commit**

  ```bash
  git add UI/MainWindow.xaml.cs
  git commit -m "feat(menu): 唤醒时加入淡入与微缩放动画"
  ```

---

### Task 7：更新 UI 文档

**Files:**
- Modify: `docs/03-ui-and-styling.md:19-41`

- [ ] **Step 1：更新 MainWindow 视觉描述**

  将 §2 中“方块矩阵布局”与“定位”“显隐”之间的段落更新为：

  ```markdown
  ### 方块矩阵布局
  `ScrollViewer` + `ItemsControl`(ItemsPanel=`WrapPanel`)，每个动作为 76×76 图标方块（`MenuButtonStyle` + 内含图标 `TextBlock`（字形绑定 `ActionItem.Icon` 经 `HexToGlyphConverter`，24px）+ 名称 `TextBlock`（11px，`#E0FFFFFF`））；按钮带 1px 半透明边框、hover 时 1.05 微缩放。底部工具区含齿轮按钮，使用 `MenuSettingsButtonStyle`。面板外层由双层 `Border` 实现：底层为 `BlurEffect` 柔和投影（`IsHitTestVisible=False`），顶层 `RootBorder` 承载内容与半透明深灰背景。

  ### 定位
  钩子事件给出物理坐标，用 `ToLogical(POINT)`（封装 `TransformFromDevice`）转逻辑坐标后令窗口中心对齐光标。`ToLogical` 同时供 `OnAnyMouseDown` 复用，统一 DPI 处理。
  ```

- [ ] **Step 2：Commit**

  ```bash
  git add docs/03-ui-and-styling.md
  git commit -m "docs(menu): 更新 MainWindow 视觉描述"
  ```

---

### Task 8：构建与手工验证

**Files:**
- N/A（运行验证）

- [ ] **Step 1：Release 构建**

  Run: `dotnet build -c Release`
  Expected: 0 errors，0 warnings（与原有警告一致）。

- [ ] **Step 2：启动应用并验证默认外观**

  Run: `dotnet run -c Release`（或执行生成的 `.exe`）
  验证项：
  - [ ] 唤醒后菜单尺寸约为 250×250，背景为深灰半透明。
  - [ ] 面板边缘有 1px 细边框，整体有柔和投影。
  - [ ] 按钮为 76×76，图标大小适中，文字清晰。
  - [ ] 鼠标悬停按钮时按钮轻微放大并提亮。
  - [ ] 鼠标按下按钮时按钮背景变暗。
  - [ ] 底部齿轮为圆角小按钮，hover 有反馈。
  - [ ] 唤醒时有淡入动画，不卡顿。
  - [ ] 点击外部、执行动作、点击齿轮后菜单立即隐藏。

- [ ] **Step 3：验证配置迁移**

  - [ ] 关闭应用，备份并删除 `settings.json`，重新启动：应使用新默认值（250×250、新背景色等）。
  - [ ] 在设置页修改菜单宽度为 300 并应用，关闭重启：应保留自定义宽度，其他视觉改进仍然生效。

- [ ] **Step 4：Commit（如验证通过）**

  如全部验证通过，当前分支已包含所有提交，无需额外 commit。

---

## 自检

### Spec 覆盖检查

| Spec 要求 | 对应任务 |
|-----------|----------|
| 默认尺寸 250×250、背景 #E6202020 | Task 1 |
| 按钮 76×76、细边框、hover 1.05 / pressed 0.98 | Task 2 |
| 底部齿轮圆角按钮 | Task 3 + Task 5 |
| 面板阴影与细边框 | Task 4 |
| 唤醒淡入 + 微缩放动画 | Task 6 |
| 老用户自定义值保留 | Task 1（默认值更新不触发覆盖） |
| 文档同步 | Task 7 |

### Placeholder 检查

- 无 TBD/TODO。
- 所有代码块均为可直接使用的 XAML/C#。
- 所有命令均附带预期输出。

### 一致性检查

- `MenuButtonStyle` 中使用的 `DynamicResource` 键名（`MenuButtonBackgroundBrush`、`MenuButtonHoverBackgroundBrush`）与 `MainWindow.ApplyMenuSettings` 中注入的键名一致。
- `RootBorder.RenderTransform` 在构造函数初始化为 `ScaleTransform`，在 `WakeUp` 中通过类型转换复用，无空引用风险。
- `Sleep()` 中清除 Opacity 与 Scale 动画后立刻设置 `Opacity=0`，行为正确。

---

## 执行交接

**Plan complete and saved to `docs/superpowers/plans/2026-07-04-wake-panel.md`.**

Two execution options:

1. **Subagent-Driven (recommended)** - Dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach would you like?
