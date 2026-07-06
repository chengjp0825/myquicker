# 唤醒面板网格布局与滚动条优化设计

> 设计日期：2026-07-06  
> 范围：MyQuicker 唤醒菜单（MainWindow）的网格列数配置与滚动条视觉优化  
> 依据：既有 `docs/superpowers/specs/2026-07-04-wake-panel-design.md` 及用户反馈

## 1. 背景与目标

用户反馈：
1. 唤醒面板应支持可选网格布局，希望有 **2×2** 与 **3×3** 两种。
2. 当前面板的上下滚动条 UI 太丑。

本次目标：
- 在 `MenuSettings` 中增加全局网格列数配置（2 或 3），面板尺寸固定，按钮自动填满每个格子。
- 超出可见格子的动作纵向滚动，滚动条改为极简暗色、自动隐藏风格。
- 不破坏现有配置系统：老用户 `settings.json` 缺失该字段时默认使用 3×3。
- 保持 MainWindow 单例预热、屏幕外瞬移、Opacity 显隐等既有生命周期规范。

## 2. 设计决策

| 决策 | 说明 |
|------|------|
| 继续用 `WrapPanel` 作为 `ItemsPanel` | 通过动态计算按钮宽高来强制每行固定列数，实现简单，无需引入 `UniformGrid` 的生成面板绑定问题。 |
| 按钮尺寸由 `GridColumns` 动态计算 | 面板宽高固定，2×2 时按钮变大、3×3 时按钮变小，始终填满可见区域。 |
| 图标使用 `Viewbox` 包裹 | 让 Segoe MDL2 Assets 图标随按钮大小自动缩放，2×2 下图标更大、3×3 下图标更小，文字保持 11px 不变。 |
| 滚动条自定义 `ScrollBar` 样式 | 8px 宽透明轨道、4px 白色圆角 thumb、默认 `Opacity="0"`、悬停时 `Opacity="1"`，实现自动隐藏。 |
| 数据模型用整数 `GridColumns` | 默认 3，由 `SettingsBuilder` 限制为 2~3，与现有 `Width/Height` 等数值设置风格一致。 |

## 3. 数据模型与配置持久化

### 3.1 `MenuSettings` 新增字段

```csharp
public sealed class MenuSettings
{
    // ... 现有字段 ...

    /// <summary>唤醒菜单网格列数（2 或 3）。</summary>
    public int GridColumns { get; set; } = 3;
}
```

### 3.2 归一化与校验

在 `SettingsBuilder.NormalizeMenuSettings` 中：

```csharp
menu.GridColumns = Math.Clamp(menu.GridColumns, 2, 3);
```

### 3.3 迁移策略

- 老用户 `settings.json` 若缺失 `GridColumns`，`MenuSettings` 的默认值 `3` 自动生效，即 3×3。
- 不引入配置版本号，也不强制覆盖旧值。

### 3.4 ViewModel 同步

`SettingsViewModel.Copy(MenuSettings src, MenuSettings dst)` 增加：

```csharp
dst.GridColumns = src.GridColumns;
```

## 4. 主窗口布局

### 4.1 XAML 变更（`UI/MainWindow.xaml`）

动作按钮的 `Width` / `Height` 从固定 76 改为动态资源：

```xaml
<Button Click="ActionButton_Click"
        Style="{StaticResource MenuButtonStyle}"
        Width="{DynamicResource MenuButtonWidth}"
        Height="{DynamicResource MenuButtonHeight}"
        Margin="5"
        Padding="4"
        Cursor="Hand">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <Viewbox Grid.Row="0" Stretch="Uniform" Margin="4">
            <TextBlock FontFamily="Segoe MDL2 Assets"
                       Text="{Binding Icon, Converter={StaticResource HexToGlyphConverter}}" />
        </Viewbox>

        <TextBlock Grid.Row="1"
                   Text="{Binding Name}"
                   FontSize="11"
                   Foreground="#E0FFFFFF"
                   TextAlignment="Center"
                   TextTrimming="CharacterEllipsis"
                   Margin="0,2,0,1" />
    </Grid>
</Button>
```

`ScrollViewer` 应用自定义滚动条样式：

```xaml
<ScrollViewer Grid.Row="0"
              VerticalScrollBarVisibility="Auto"
              HorizontalScrollBarVisibility="Disabled">
    <ScrollViewer.Resources>
        <Style TargetType="ScrollBar" BasedOn="{StaticResource MenuScrollBarStyle}" />
    </ScrollViewer.Resources>
    <ItemsControl x:Name="ActionsControl" ...>
        <!-- ItemsPanel 仍使用 WrapPanel -->
    </ItemsControl>
</ScrollViewer>
```

### 4.2 按钮尺寸计算（`UI/MainWindow.xaml.cs`）

在 `ApplyMenuSettings(MenuSettings menu)` 中增加：

```csharp
const double rootPaddingH = 24; // 左 14 + 右 10
const double rootPaddingV = 24; // 上 12 + 下 12
const double bottomBarHeight = 44;
const double buttonMargin = 10; // 左右或上下各 5

int columns = Math.Clamp(menu.GridColumns, 2, 3);
int visibleRows = columns; // 2×2 或 3×3

double contentWidth = Math.Max(menu.Width - rootPaddingH, 1);
double actionsHeight = Math.Max(menu.Height - rootPaddingV - bottomBarHeight, 1);

// 每个格子可用空间 = 面板可用空间 / 行列数
// 按钮实际尺寸要再减去 margin 占用的部分
double cellWidth = contentWidth / columns;
double cellHeight = actionsHeight / visibleRows;

double buttonWidth = cellWidth - buttonMargin;
double buttonHeight = cellHeight - buttonMargin;

Resources["MenuButtonWidth"] = Math.Max(buttonWidth, 32);
Resources["MenuButtonHeight"] = Math.Max(buttonHeight, 32);
```

> 常量 `rootPaddingH`、`rootPaddingV`、`bottomBarHeight` 必须与 `MainWindow.xaml` 中的 `Padding`、`Margin` 和底部工具区实际高度保持一致。若后续调整 UI，这些常量需要同步修改。

## 5. 滚动条样式

在 `Resources/ThemeStyles.xaml` 中新增：

```xaml
<!-- 唤醒菜单滚动条：极简暗色、自动隐藏 -->
<Style x:Key="MenuScrollBarThumbStyle" TargetType="Thumb">
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="Thumb">
                <Border Background="#B0FFFFFF"
                        CornerRadius="3"
                        Width="4"
                        HorizontalAlignment="Center" />
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>

<Style x:Key="MenuScrollBarPageButtonStyle" TargetType="RepeatButton">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="RepeatButton">
                <Border Background="Transparent" />
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>

<Style x:Key="MenuScrollBarStyle" TargetType="ScrollBar">
    <Setter Property="Width" Value="8" />
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="Opacity" Value="0" />
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="ScrollBar">
                <Grid Background="Transparent" SnapsToDevicePixels="True">
                    <Track x:Name="PART_Track" IsDirectionReversed="True">
                        <Track.DecreaseRepeatButton>
                            <RepeatButton Command="ScrollBar.PageUpCommand"
                                          Style="{StaticResource MenuScrollBarPageButtonStyle}" />
                        </Track.DecreaseRepeatButton>
                        <Track.IncreaseRepeatButton>
                            <RepeatButton Command="ScrollBar.PageDownCommand"
                                          Style="{StaticResource MenuScrollBarPageButtonStyle}" />
                        </Track.IncreaseRepeatButton>
                        <Track.Thumb>
                            <Thumb Style="{StaticResource MenuScrollBarThumbStyle}" />
                        </Track.Thumb>
                    </Track>
                </Grid>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
    <Style.Triggers>
        <Trigger Property="IsMouseOver" Value="True">
            <Setter Property="Opacity" Value="1" />
        </Trigger>
    </Style.Triggers>
</Style>
```

行为：
- 默认透明隐藏，鼠标移到滚动条区域（窗口右侧 8px）时显示。
- 支持鼠标滚轮、拖动 thumb、点击轨道翻页。

## 6. 设置页

### 6.1 `UI/SettingsWindow.xaml`

在「菜单」设置页增加「网格布局」：

```xaml
<Grid Margin="0,0,0,12">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="200" />
    </Grid.ColumnDefinitions>
    <StackPanel Grid.Column="0">
        <TextBlock Style="{StaticResource FieldTitle}" Text="网格布局" />
        <TextBlock Style="{StaticResource FieldDesc}" Text="唤醒菜单的格子列数" />
    </StackPanel>
    <ComboBox Grid.Column="1" x:Name="MenuGridColumnsCombo"
              Style="{StaticResource FlatComboBox}"
              Width="200" HorizontalAlignment="Right" SelectedIndex="1">
        <ComboBoxItem Content="2 × 2" />
        <ComboBoxItem Content="3 × 3" />
    </ComboBox>
</Grid>
```

### 6.2 `UI/SettingsWindow.xaml.cs`

`PopulateControls` 中增加：

```csharp
MenuGridColumnsCombo.SelectedIndex = _viewModel.Menu.GridColumns == 2 ? 0 : 1;
```

`SaveAndCloseAsync` 中增加：

```csharp
_viewModel.Menu.GridColumns = MenuGridColumnsCombo.SelectedIndex == 0 ? 2 : 3;
```

## 7. 文件变更清单

| 文件 | 变更类型 | 说明 |
|------|----------|------|
| `src/Domain/DTO/MenuSettings.cs` | 修改 | 新增 `GridColumns` 字段，默认 3。 |
| `src/Services/SettingsBuilder.cs` | 修改 | `NormalizeMenuSettings` 限制 `GridColumns` 为 2~3。 |
| `src/UI/SettingsViewModel.cs` | 修改 | `Copy(MenuSettings)` 同步 `GridColumns`。 |
| `UI/SettingsWindow.xaml` | 修改 | 菜单页增加「网格布局」下拉。 |
| `UI/SettingsWindow.xaml.cs` | 修改 | 下拉框读写与保存逻辑。 |
| `UI/MainWindow.xaml` | 修改 | 按钮宽高绑定动态资源，图标用 `Viewbox`，滚动条应用自定义样式。 |
| `UI/MainWindow.xaml.cs` | 修改 | `ApplyMenuSettings` 计算并注入 `MenuButtonWidth/Height`。 |
| `Resources/ThemeStyles.xaml` | 修改 | 新增 `MenuScrollBarStyle` 等滚动条样式。 |

## 8. 验收标准

- [ ] 设置页「菜单」页出现「网格布局」下拉，选项为 2×2 / 3×3。
- [ ] 切换后保存，唤醒菜单立即按新网格布局显示，无需重启。
- [ ] 2×2 时按钮更大、3×3 时按钮更小，始终填满固定尺寸面板。
- [ ] 图标随按钮大小自适应缩放，文字保持清晰可读。
- [ ] 动作数量超过可见格子数时，可通过鼠标滚轮或拖动滚动条纵向滚动查看。
- [ ] 滚动条默认隐藏，鼠标移到窗口右侧时以极简暗色样式出现。
- [ ] 老用户 `settings.json` 无 `GridColumns` 时，默认按 3×3 显示。
- [ ] `dotnet build` 与 `dotnet test` 通过，不引入新的警告。

## 9. 风险与回退

| 风险 | 影响 | 回退方案 |
|------|------|----------|
| 面板高度设置过小导致计算出的按钮尺寸过小 | 可读性下降 | 在计算中 clamp 最小 32 DIP；必要时在设置页增加最小尺寸提示。 |
| 自定义滚动条在高分屏/特殊主题下显示异常 | 滚动条不可见或错位 | 调整 `MenuScrollBarStyle` 的 `Width` 与 thumb 颜色。 |
| `bottomBarHeight` 常量与 XAML 实际高度不一致 | 可见行数偏差 | 将底部工具区高度在 XAML 中固定为 44 DIP，并在代码注释中标注同步关系。 |
| 用户已自定义 `MenuSettings.Width/Height` | 可能出现非预期格子比例 | 依然按公式计算，属于用户自定义后的自然结果；必要时后续增加“恢复默认”按钮。 |

## 10. 后续工作

本设计通过后将由 `superpowers:writing-plans` 生成详细实现计划，再进入编码阶段。
