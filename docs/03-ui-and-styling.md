# UI 风格与视觉层（UI & Styling）

> 子系统详细规范。展开 `SPEC.md` §4.2 / §4.4 中的 UI 视觉、布局与交互细节。
> 修改 UI 样式、新增页面、调整颜色或菜单布局时阅读本文件。

## 1. ThemeStyles.xaml —— 公共样式

`/Resources/ThemeStyles.xaml` 公共主题资源字典：主题画刷 / 尺寸 + 公共控件样式，由 `App.xaml` 合并，各窗口以 `StaticResource` 引用。**不写入 JSON**。

控件清单：
- `MenuButtonStyle` —— 唤醒菜单方块按钮。
- `NavRadioButton` —— Fluent：浅蓝选中底 + Accent 指示条 + 文字变 Accent；hover 0.15s 淡入。
- `ActionButton` / `FlatTextBox` / `FlatComboBox` / `ColorSwatch`
- DataGrid 系列：`DataGridColumnHeader` / `DataGridRow` / `DataGridCell`
- `FieldTitle` / `FieldDesc` / `SectionHeader`（合并页分区标题）

> 新增公共样式 → 加到 `ThemeStyles.xaml`；窗口独有视觉物理反馈保留内联（见 `docs/01-architecture-and-config.md`「统一配置三层」）。

## 2. MainWindow —— 无框唤醒菜单

### 窗口属性
`WindowStyle=None` / `AllowsTransparency=True` / `Background=Transparent` / `Topmost=True` / `ShowInTaskbar=False`。

### 不抢焦点
`OnSourceInitialized` 给 HWND 加 `WS_EX_NOACTIVATE`，弹出与点击均不夺取当前应用焦点。菜单全程不抢焦点，原前台窗口始终持焦，故**无需** `Activate()` / `OnDeactivated` / 显式恢复前台窗口，动作执行前只需 `Hide()`。

### 方块矩阵布局
`ScrollViewer` + `ItemsControl`(ItemsPanel=`WrapPanel`)，每个动作为 72×72 图标方块（`MenuButtonStyle` + 内含图标 `TextBlock` + 名称 `TextBlock`）；底部工具区含齿轮按钮。`ApplyMenuSettings` 供设置页即时刷新菜单视觉。

### 定位
钩子事件给出物理坐标，用 `ToLogical(POINT)`（封装 `TransformFromDevice`）转逻辑坐标后令窗口中心对齐光标。`ToLogical` 同时供 `OnAnyMouseDown` 复用，统一 DPI 处理。

### 显隐与防重入
`OnHookWakeupClick` 开头两道防重入闸（菜单可见 / 截屏覆盖层开启，详见 `docs/02-interaction-engine.md`「唤醒手势防重入」）。通过后才 `PositionAtCursor` + 热重载动作 + `Show()`。`OnAnyMouseDown` 检测点击落在窗口外则 `Hide()`。

### 动作执行与热重载
- 按钮点击先 `Hide()` 再 `ActionExecutor.Execute`。
- 每次唤醒经 `ActionExecutor.GetActions()` → `ActionStore` → `SettingsManager.Load()` 重读磁盘动作列表。`Menu` 视觉参数由 `ApplyMenuSettings` 即时刷新（构造时与设置页"应用设置"后共用此路径），编辑 `settings.json` 无需重启。

### 设置入口
齿轮按钮 `Hide()` 后回调 `OpenSettingsAction`（由 `App` 接到 `SettingsWindow`）。

## 3. SettingsWindow —— Fluent 4 页设置中心

### 整体
- 内容区 `#FAFAFA` 画布；左侧边栏 `NavRadioButton`（Fluent：选中浅蓝底 `NavSelectedBrush` + 3px Accent 指示条 + 文字变 Accent；hover 0.15s 淡入）。
- 4 个页签：常规（唤醒键 `FlatComboBox` + 唤醒时拦截按键 `CheckBox` + 画圈灵敏度 `FlatComboBox`）/ 动作管理（扁平 `DataGrid`）/ 截屏与贴图（`SectionHeader` 分区标题分隔两组，含截图后行为与截图范围 `FlatComboBox`）/ 菜单。页面切换由 `BooleanToVisibilityConverter` 绑定 `RadioButton.IsChecked`，无代码后置。
- 唯一面板布局约束：750×500，保留内联。

### 表单
左标题+说明（`FieldTitle` / `FieldDesc`）、右控件（`FlatTextBox` / `FlatComboBox` + `ColorSwatch` 圆角色块），行距 12px，无卡片。

### DataGrid 扁平
透明底、仅横向网格线、列头仅下边框、行 hover `RowHoverBrush`/选中 `NavSelectedBrush`、单元格去焦点虚线框、编辑态 `EditingElementStyle=FlatTextBox`。

### 颜色字段
十六进制文本框 + 实时预览色块（`WireColorPreview` 在 `TextChanged` 刷色，无效值清空预览）；应用前 `Validate` 全字段校验，非法 `MessageBox` 拦截。

### 唤醒键下拉框
3 项：鼠标中键 / 侧键后退 (XButton1) / 单纯画圈 (无按键，对应 `WAKEUP_CIRCLE_GESTURE = -1`)；`ToIndex`/`SaveButton_Click` 适配 -1。

### 置顶穿透小技巧
`OpenSettings` 中 `Topmost=true` 后立即 `false`，绕过系统前台锁。

> 新增设置页：加一个 `RadioButton` + 对应内容区，沿用 `NavRadioButton` / `FlatTextBox` 等样式。

## 4. ScreenshotWindow —— 截屏覆盖层

覆盖层颜色由 `SettingsModel.Snipping` 注入（构造函数读 `SettingsManager.Instance.Settings.Snipping`，赋值 `MaskPath.Fill` / `HighlightBorder.BorderBrush`）；红框厚度（2px）与窗口 `Background`（Black）已硬编码，不再可配。`DragThreshold` 取自 `SnippingSettings.DragThreshold`（readonly 字段，双模态状态机逻辑不变）。

### 三层结构（`RootGrid`）
1. `BackgroundImage` —— 全屏底图（`Stretch="None"`）；
2. 暗罩 `Path`（`MaskPath`）—— `MaskColor=#66000000`，用 `CombinedGeometry(Exclude)` 在 `ScreenGeometry`（整屏）中挖出 `CutoutGeometry`（选区）形成镂空；
3. `HighlightBorder` —— 选区红框（`BorderColor=#FF0000` / `BorderThickness=2`），默认 `Hidden`。

### 双模态状态机
`DragThreshold` 解耦点击/拖拽（默认 5 DIP）。

**寻边模式**（未跨阈值，`OnMouseMove`）：
1. `WindowUnderCursor` 临时给自身 HWND 加 `WS_EX_TRANSPARENT`（使 `WindowFromPoint` 穿透覆盖层），取到光标下窗口后**立即还原** ex style；
2. `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` 取真实物理窗口矩形（Win11 阴影修正），失败回退 `GetWindowRect`；
3. 桌面背景（`Progman`/`WorkerW`）视为空点不截图；
4. 转窗口局部坐标后 `ApplySelection`：同时设置 `CutoutGeometry.Rect`（镂空）与 `HighlightBorder` 的 `Margin`/`Width`/`Height`（红框）。

**拖拽模式**（左键按下后位移超阈值）：`CaptureMouse`，用 `min/abs` 归一化起止点矩形，支持反向拖拽。

### 结算（`SettleSelection`，松开左键时）
模式 A 智能快照（未拖拽且有红框 → 截红框）/ 模式 B 手动拖拽（截拖拽选区）/ 空点（无红框且未拖拽 → 不操作）。`Math.Clamp` 把裁剪矩形夹取到 base-image 边界 → `CroppedBitmap` 裁剪 → `Freeze`。结算动作由 `SnippingSettings.AfterScreenshot` 决定：`PinAndCopy`（默认）写剪贴板 + 钉贴图 / `CopyOnly` 仅 `Clipboard.SetImage`（try-catch，剪贴板被独占不阻断）/ `PinOnly` 仅 `new PinWindow(crop, screenX, screenY).Show()` 联动钉图（截图罩关闭后贴图存活）。`OnMouseLeftButtonUp` 用 try/finally 保证 `Close()` 与 `ReleaseMouseCapture`。

ESC / 右键取消关闭。

> 越界夹取与 try/finally 的健壮性细节见 `docs/02-interaction-engine.md`「崩溃兜底」；多屏坐标基准见同文件「多屏坐标系」。

## 5. PinWindow —— 贴图引擎

### 窗口属性
`WindowStyle=None` / `AllowsTransparency=True` / `Topmost=True` / `ShowInTaskbar=True` / `ResizeMode=CanResize`。

### 两层结构
`PinBorder`（边框层，向外生长）+ `PinImage`（`Stretch=None`，`Margin=border` 向内缩，内容面积恒为 imgW×imgH）；两层 `IsHitTestVisible=False`，命中测试落到 Window。`ResizeMode=CanResize` 但图片 `Stretch=None` 不随窗口缩放，"重置大小"仅重算窗口外接矩形。

### 交互
左键 `DragMove`（系统模态移动，无抖动）/ 左键双击关闭 / 右键菜单。

### 右键菜单
置顶 / 显示阴影 / 显示边界 / 重置大小 / 不透明度（0.3/0.5/0.8/1.0）/ 旋转 / 镜像 / 复制图片 / 另存为… / 作为文件打开 / 关闭。

### 变换
- 旋转：`_rotationStep = (_rotationStep + 1) % 4`，`RotationAngle = step * 90`（步进固定 90°，原 `RotationStepDegrees` 已移除：非 90° 会破坏 90/270 宽高互换逻辑），90/270 时窗口宽高互换（`ApplyWindowSize`）。
- 镜像：`ScaleTransform.ScaleX = -1/1`（水平翻转，`RenderTransformOrigin=0.5,0.5` 居中）。
- 显示边界：`PinBorderThickness` 0↔2，边框向外生长（窗口左上角反向偏移保持图片屏幕坐标不变）。
- 不透明度：直接设 `Window.Opacity`。

### 落盘
另存为 / 作为文件打开：`PngBitmapEncoder` 落盘；后者写临时文件后 `UseShellExecute=true` 打开。

### 参数注入
由 `SettingsModel.Pin` 注入（`BorderColor` / `DefaultOpacity` / `DefaultShowBorder` / `DefaultAnnotationMode` / `DefaultTopmost` / `DefaultShowShadow`）；最小宽高（40×40）、阴影 Depth/Opacity/Direction/Color/`BlurRadius=14`、旋转步进（90°）、`PinBorderThickness=2`、不透明度菜单预设保留内联。

## 6. PinWindow 批注工具栏与默认外观

贴图窗口叠加基础图片批注（画框 / 画圆 / 箭头 / 文字），支持画笔粗细与颜色。状态机与光栅化导出见 `docs/02-interaction-engine.md`「PinWindow 批注状态机与光栅化导出」。

### 图层结构（`RootGrid`）
- `PinBorder`（边框层，`IsHitTestVisible=False`）—— 不变。
- `ContentRoot`（`Grid`，`Margin=border`，`Background={x:Null}`）—— **`RenderTargetBitmap` 导出根**。`Background={x:Null}` 使本层不拦截命中，None 模式左键击穿到 Window 走 `DragMove`。
  - `PinImage`（`Margin=0`，`IsHitTestVisible=False`，保留 Scale+Rotate / Shadow）。
  - `AnnotationCanvas`（`Background=Transparent`，`IsHitTestVisible` 按 `EditMode` 与批注模式开关切换）—— 批注绘制层，铺满 `ContentRoot` 即图片视觉区，与底图 1:1 对齐。
- `AnnotationToolbar`（`Border→StackPanel`，右上角悬浮）—— **在 `ContentRoot` 之外，不参与导出**。

### 默认外观（可配置）
- **默认显示边界**：`PinSettings.DefaultShowBorder`（默认 true）—— 钉图时默认开启 2px 边框（向外生长）。右键「显示边界」初始勾选与之同步。
- **默认批注模式**：`PinSettings.DefaultAnnotationMode`（默认 false）—— 钉图时默认是否开启批注模式。
- **默认置顶**：`PinSettings.DefaultTopmost`（默认 true）—— 钉图时默认置顶在前。右键「置顶」初始勾选与之同步。
- **默认显示阴影**：`PinSettings.DefaultShowShadow`（默认 true）—— 钉图时默认开启投影。右键「显示阴影」初始勾选与之同步。四项均在设置中心「截屏与贴图」页编辑，下次钉图生效。

### 工具栏（仅批注模式开启时 Hover 显隐）
- **批注模式开关**：右键「批注 ▸ 批注模式」勾选项切换。关闭时工具栏完全不存在（`Opacity=0` 且 `IsHitTestVisible=False`，Hover 不触发），Canvas 命中关闭、左键回退 `DragMove`。开启时才进入 Hover 显隐。
- **Hover 触发**：批注模式开启后，鼠标进入窗口 → 工具栏 `Opacity` 淡入（0→1，0.15s）并 `IsHitTestVisible=True`；移出 → 淡出并 `IsHitTestVisible=False`。默认画面绝对干净。
- **布局**：`StackPanel Orientation=Horizontal`：指针 / 画框 / 画圆 / 箭头 / 文字 ｜ 画笔粗细（细2/中4/粗6）｜ 颜色预设（红/黄/蓝/绿/白）。每个按钮带 `ToolTip` 说明。
- **样式**：`FlatIconButton`（图标 `RadioButton`，选中 Accent 底；画笔粗细复用此样式，实例覆盖 `GroupName`/`FontFamily`/`FontSize`）+ `FlatColorSwatch`（圆形色块 `RadioButton`），集中定义于 `ThemeStyles.xaml`（禁止内联）。
- **坐标空间**：视图空间（WYSIWYG）—— 批注绘制在 Canvas 局部坐标，导出即所见。

> `ApplyWindowSize` 改动：原 `PinImage.Margin = border` 改为 `ContentRoot.Margin = border`，`PinImage` 改 `Margin=0`。旋转 / 边框外扩逻辑不变。

## 7. MainWindow 极速唤醒渲染规范

参考顶级效率工具（如 Quicker）的底层设计，MainWindow 采用「单例预热 + 屏幕外瞬移」机制，规避 DWM 表面重新分配与 XAML 首次渲染延迟，使唤醒到可见 < 1 帧。

### 7.1 单例与生命周期
- MainWindow 为**全局单例**，`App.OnStartup` 实例化后常驻整个进程生命周期，**禁止运行期间销毁重建**（禁止 `Close()` / 重新 `new`）。
- 唤醒 / 隐藏只切换物理坐标与透明度，窗口 HWND 与可视树恒存在。

### 7.2 预热（Pre-warm）
- `App.OnStartup` 中尽早实例化 MainWindow，设 `WindowStartupLocation=Manual`、`Left=Top=-9999`、`Opacity=0`，随即调用一次 `Show()`。
- 强迫 WPF 在后台完成 XAML 解析、模板绑定、GPU 材质编译；窗口已渲染在内存，用户不可见。

### 7.3 显隐规则（禁用 Show / Hide / Visibility）
- **禁止** `Window.Show()` / `Window.Hide()` / 切换 `Visibility` —— 触发 DWM 重新分配表面，引入可感知延迟。
- **隐藏 `Sleep()`**：`Opacity=0` + `Left=Top=-9999`（丢出屏幕外）。
- **唤醒 `WakeUp(POINT)`**：
  1. `ToLogical(POINT)` 取光标逻辑坐标；
  2. 按 `ActualWidth` / `ActualHeight` 计算居中，赋值 `Left` / `Top`；
  3. `Opacity=1`；
  4. `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)` 重申置顶不抢焦。
- 显隐状态由 `_isAwake` 布尔跟踪，**不依赖 `Window.IsVisible`**（窗口预热后始终 `IsVisible=true`）。

### 7.4 唤醒链路零 IO
- `WakeUp` 路径**禁止任何磁盘 IO**（不读 `settings.json`）。
- Action 数据在 `App.OnStartup` 加载到内存缓存，`SettingsWindow` 保存时刷新缓存；唤醒仅改变坐标与透明度。

## 8. ToastWindow —— 轻量瞬时通知

无框置顶 toast，用于剪贴板失败、启动成功等非阻塞提示。`Toast.Show(message, durationMs=2500)` 静态入口（UI 线程调用），窗口生命周期自管。

### 窗口属性
`WindowStyle=None` / `AllowsTransparency=True` / `Topmost=True` / `ShowInTaskbar=False` / `ShowActivated=False`（不抢焦点）/ `SizeToContent=WidthAndHeight`（自适应文案）。

### 视觉
暗色半透明卡片（`#E6323232`，圆角 8px，`MaxWidth=360`）+ 白字 13px 自动换行。

### 定位与堆叠
主屏工作区右下角（`SystemParameters.WorkArea`），多个 toast 向上堆叠（贴着已有 toast 上方 10px 间隙）。`Loaded` 时按 `ActualWidth/Height` 定位。

### 显隐
`Opacity=0` 起始 → `Loaded` 淡入 0→1（150ms）→ `DispatcherTimer` 到 `durationMs` 淡出 1→0（200ms）→ `Close()`。`Closed` 从静态 `_active` 列表移除（列表同时防 GC 回收）。

### 调用点
- `App.OnStartup` 末尾：`Toast.Show($"MyQuicker 已启动 · {hint}唤醒")`——主窗口无任务栏入口，需告知已启动 + 当前唤醒方式（中键/侧键/画圈）。
- `ScreenshotWindow.SettleSelection` 剪贴板失败 catch：`Toast.Show("⚠ 剪贴板被占用，截图未复制", 3000)`——原仅 `Debug.WriteLine` 用户无感，`CopyOnly` 模式下截图会彻底丢失无提示。
