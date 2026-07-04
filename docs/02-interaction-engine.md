# 核心交互与底层机制（Interaction Engine）

> 子系统详细规范。展开 `SPEC.md` §4.1 / §4.5 中的底层钩子、坐标、手势、GDI 与健壮性机制。
> 修改底层钩子、截屏坐标、手势算法、GDI 回收或崩溃兜底时阅读本文件。

## 1. NativeMethods 索引（P-Invoke）

`Interop/NativeMethods.cs` 静态类集中定义全部非托管 API。**禁止把 P-Invoke 写进 UI code-behind。** 新增 API 一律加到此文件并附 XML 注释。

### 1.1 Hook Definitions
- `SetWindowsHookEx`：注册钩子过程，目标 `WH_MOUSE_LL` (id=14)。
- `UnhookWindowsHookEx`：卸载钩子，退出时显式调用防泄漏。
- `CallNextHookEx`：传递给下一钩子。
- `LowLevelMouseProc` 委托类型；`GetModuleHandle` 取当前模块句柄。

### 1.2 Coordinate Control
- `GetCursorPos(out POINT)`：光标物理屏幕坐标。
> 焦点 API（`GetForegroundWindow` / `SetForegroundWindow`）已删除：菜单走 `WS_EX_NOACTIVATE` 全程不抢焦点，无需恢复前台窗口。

### 1.3 Window Styles (No-Activate / Hit-Test-Transparent)
- `GetWindowLong` / `SetWindowLong`（32/64 位自适配分发）。
- `WS_EX_NOACTIVATE`：窗口不抢焦点（`MainWindow` 在 `OnSourceInitialized` 加注）。
- `WS_EX_TRANSPARENT`：命中测试穿透（`ScreenshotWindow` 寻边时临时加上、取目标后立即还原）。

### 1.4 GDI Memory Management
- `DeleteObject`：释放 `Bitmap.GetHbitmap()` 创建的非托管 `HBITMAP`。

### 1.5 Window Edge Detection (Smart Snipping)
- `WindowFromPoint`：取光标下窗口。
- `GetWindowRect` / `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`：取窗口物理矩形（Win11 阴影修正，失败回退 `GetWindowRect`）。
- `GetClassName`：过滤桌面背景（`Progman` / `WorkerW`），使空点不截图。

### 1.6 Structs and Constants
- 消息常量：`WM_MOUSEMOVE` / `WM_LBUTTONDOWN` / `WM_RBUTTONDOWN` / `WM_NCLBUTTONDOWN` / `WM_MBUTTONDOWN` / `WM_XBUTTONDOWN`。
- `POINT` / `MSLLHOOKSTRUCT` / `RECT`：`[StructLayout(LayoutKind.Sequential)]` 精确映射内存。

## 2. 全局鼠标钩子（`GlobalHookService`）

### 初始化与线程约束
- `Process.GetCurrentProcess().MainModule.ModuleName` → `GetModuleHandle` → `SetWindowsHookEx`。
- 钩子必须在消息泵线程（主 UI 线程）安装；`App.OnStartup` 调 `Start()`，`App.OnExit` 调 `Stop()` + `Dispose()`。
- **钩子委托必须存字段（`_hookProc`）防 GC 回收**，否则 user32 回调野指针会崩进程。

### 拦截逻辑（`HookCallback`）
1. **`WM_MOUSEMOVE` 旁观分支**（永不拦截，始终 `CallNextHookEx`）：仅在 `WakeupMessage == WAKEUP_CIRCLE_GESTURE` 时入队 `(POINT, Timestamp=Environment.TickCount64)` 到 `_moveHistory`，`PruneMoveHistory` 剔除 >800ms 老点；样本数 ≥8 且距上次检测 ≥30ms 时（**节流**，避免每个 mousemove 都复制历史 + 跑 `Atan2` 拖慢钩子线程致鼠标卡顿）复制到复用缓冲 `_pointsBuffer` 调 `GestureHelper.IsCircle`，命中即清空队列并 `Dispatcher.BeginInvoke(OnWakeupClick)`。
2. 跟踪左/右/非客户区/中/侧键按下；任一按下即 `Dispatcher.BeginInvoke(OnAnyMouseDown)`（供 UI 检测菜单外点击）。
3. 若 `msg == ActionSettings.WakeupMessage`：侧键（`WM_XBUTTONDOWN`）还需高位字 `mouseData >> 16` 等于 `XButtonData`；命中即 `Dispatcher.BeginInvoke(OnWakeupClick)` 并 `return (IntPtr)1` 吞键；否则 `CallNextHookEx`。

### 回调时效
除"吞键"同步返回外，所有 UI 变化与磁盘 IO 一律异步派发，保证 <100ms 返回，规避 `LowLevelHooksTimeout` 静默摘钩。

### 可配置唤醒方式
中键 / 侧键后退 (XButton1) / 单纯画圈（`WAKEUP_CIRCLE_GESTURE = -1`），由 `ActionSettings.WakeupMessage` + `XButtonData` 决定，`SettingsWindow` 编辑后经 `UpdateSettings` 即时生效。

## 3. 画圈手势识别（`GestureHelper`）

**纯几何纯函数** `IsCircle(List<POINT> recentPoints)`，无状态、无副作用，O(n) 单趟。
- 调用方（`GlobalHookService`）负责维护 ≤800ms 滑动时间窗；本方法只做空间几何判定。
- 判定闸门（防误触）：
  1. 样本数 `< 8` → false。
  2. Bounding Box 宽高均 `≥80px`，宽高比 `0.5~2.0` → 否则 false（排除抖动/长条拖拽）。
  3. **有符号偏转角累加（转向数）**：相邻向量 `atan2` 差归一化到 `[-π,π]` 后求和；`|总和| ≥ 300°` → true。一致方向画圈 ≈ ±360°；直线/折返正负抵消 ≈ 0°。
  4. 向量长度 `<2px` 跳过（过滤亚像素抖动）。

## 4. 多屏截图采集（`ScreenshotService`）

### GDI 对象手动回收
`ScreenshotService.Capture()` 中 `Bitmap.GetHbitmap()` 创建的非托管 `HBITMAP` **必须**手动释放，否则 GDI 句柄泄漏：
1. `Imaging.CreateBitmapSourceFromHBitmap(...)` 后立即 `source.Freeze()` —— 强制 WPF 拷贝像素，使 HBITMAP 可安全释放；
2. 在 `finally` 块中调用 `NativeMethods.DeleteObject(hBitmap)`。

> 任何新增的 GDI 对象（`Bitmap`/`HBITMAP`/`HICON` 等）都遵循同一规则：`Freeze` 拷贝 → `DeleteObject` 释放。`NativeMethods.DeleteObject` 已就绪。

### Capture 流程
`Capture()`：`ComputeVirtualBounds()` 取所有屏幕 `Min(X/Y)` 与 `Max(Right/Bottom)` 构成虚拟屏矩形（X/Y 可能为负）→ `Bitmap` (32bppArgb) → `CopyFromScreen` → `GetHbitmap` → `Imaging.CreateBitmapSourceFromHBitmap` → `Freeze()` → `finally` 中 `DeleteObject(hBitmap)`。返回 `(BitmapSource, Rectangle)`。

## 5. 多屏坐标系（VirtualBounds）

多屏环境下主屏左侧/上方的显示器会使原点为负。所有跨屏坐标计算必须以 `ScreenshotService.ComputeVirtualBounds()` 为基准（取所有屏幕 `Min(X/Y)` 与 `Max(Right/Bottom)`，X/Y 可能为负）：
- `ScreenshotWindow` 的 `Left/Top/Width/Height` 直接绑定 `bounds`（物理像素）；
- 鼠标物理坐标转窗口局部坐标统一用 `pt - _bounds.X/Y`，窗口局部坐标与底图像素 1:1 对应，`CroppedBitmap` 即按此裁剪；
- `MainWindow` 定位用 `ToLogical(POINT)`（封装 `PresentationSource.CompositionTarget.TransformFromDevice`）把物理坐标转逻辑坐标后再居中。

> 不要用 `SystemParameters.PrimaryScreenWidth` 或单屏 `Bounds` 做跨屏计算。

> **DPI 注意**：`ScreenshotWindow` / `PinWindow` 直接用物理像素设 `Left/Top/Width/Height`，依赖 96 DPI（100% 缩放）下 DIP 与物理像素 1:1 的假设；非 100% 缩放下覆盖层/贴图定位会偏移。`MainWindow` 走 `TransformFromDevice` 不受影响。

## 6. 唤醒手势防重入

`MainWindow.OnHookWakeupClick` 开头三道闸（任一命中即 `return`，不唤醒）：
1. `if (_isAwake) return;` —— 菜单已唤醒时，后续唤醒动作（画圈/按键）一律无效（不重弹、不关闭）；睡眠靠点外面或点动作。（窗口预热后 `IsVisible` 恒 true，故用 `_isAwake` 标志跟踪，docs/03 §7.3。）
2. `if (Application.Current.Windows.OfType<ScreenshotWindow>().Any()) return;` —— 截屏覆盖层开启时不抢唤醒。
3. `if (Application.Current.Windows.OfType<SettingsWindow>().Any()) return;` —— 设置页开启时不抢唤醒，避免编辑配置时误触菜单、再经齿轮开出第二个设置页（`OpenSettings` 亦为单例）。

## 7. 崩溃兜底与健壮性

- **全局崩溃兜底**：`App` 注册 `DispatcherUnhandledException`，未捕获异常记 `Debug.WriteLine` 后 `e.Handled = true`，保活常驻托盘进程（StackOverflow/OOM 等不可恢复异常不触发此事件）。
- **越界防御**：`ScreenshotWindow.SettleSelection` 用 `Math.Clamp` 把裁剪矩形夹取到 base-image 边界，防 `CroppedBitmap` 越界抛 `ArgumentException`（寻边窗口可能超出虚拟屏）。
- **剪贴板/进程容错**：`Clipboard.SetImage` try-catch（剪贴板被独占不阻断）；`ActionExecutor` 空命令校验（`IsNullOrWhiteSpace`）+ `Process.Start` try-catch 拦 `Win32Exception`。
- **资源释放**：`ScreenshotWindow.OnMouseLeftButtonUp` try/finally 保证 `Close()` / `ReleaseMouseCapture`（无论是否抛异常）。
- **原子配置**：`SettingsManager.Save` tmp+`File.Move` 原子覆盖，防断电/崩溃截断；脏 JSON 备份 `.bak` 后回退默认值，不丢失坏文件。详见 `docs/01-architecture-and-config.md`「配置系统」。
- **钩子时效**：`HookCallback` 异步派发 UI/IO，<100ms 返回，防 `LowLevelHooksTimeout` 静默摘钩。

## 8. PinWindow 批注状态机与光栅化导出

### 8.1 批注模式与状态机（`EditMode`）
`PinWindow` 定义 `EditMode { None, Rect, Circle, Arrow, Text }`。批注模式由右键「批注 ▸ 批注模式」开关控制（默认取自 `PinSettings.DefaultAnnotationMode`）。`AnnotationCanvas.IsHitTestVisible = 批注模式开启 && mode != None`；批注模式关闭时工具栏不存在、Canvas 击穿、左键回退 `DragMove`。

- **None**：Canvas 击穿，左键落到 Window `DragMove`（拖拽 / 双击关闭不变）。
- **Rect**：`PreviewMouseDown` 起点建临时 `Rectangle`（`Stretch=Fill`），`PreviewMouseMove` 实时 `min/abs` 归一化宽高（支持反向拖拽），`PreviewMouseUp` 定型（`Stroke` = 当前颜色，`StrokeThickness` = 画笔粗细，`Fill=Transparent`）。
- **Circle**：`PreviewMouseDown` 起点建临时 `Ellipse`（`Stretch=Fill`），`PreviewMouseMove` 实时取 `min(|dx|,|dy|)` 作为直径（**真圆**，从起点向拖拽方向扩展），`PreviewMouseUp` 定型；直径 <3px 视为误触移除。
- **Arrow**：`PreviewMouseDown` 起点建临时 `Path`，`PreviewMouseMove` 实时重建几何（主线 + 末端 V 形箭头，箭头长 = max(8, 粗细×3)），`PreviewMouseUp` 定型；长度 <3px 视为误触移除。
- **Text**：`PreviewMouseDown` 在点击处生成可编辑 `TextBox`（`Background=Transparent` / `BorderThickness=0`），`LostFocus` 时转固定 `TextBlock`（保留字体 / 颜色 / 位置）；空文本移除。

> 坐标空间 = 视图空间（WYSIWYG）：批注绘制在 Canvas 局部坐标，与图片视觉区 1:1，不随 `PinImage.RenderTransform` 旋转。批注仅作覆盖层保留，不随旋转/镜像变换；需保存时右键「复制图片」/「另存为」光栅化导出。

### 8.2 `RenderTargetBitmap` 光栅化导出
复制 / 另存为 / 作为文件打开**统一改走光栅化**，弃用直接提取 `_source`：
1. **摘阴影**：渲染前 `PinImage.Effect = null` + `InvalidateVisual`，渲染后恢复原 `DropShadowEffect` 实例（防阴影烤入导出图）。
2. **DPI 缩放**（关键）：取 `VisualTreeHelper.GetDpi(ContentRoot).DpiScaleX/Y`，像素维度 = `ContentRoot.ActualWidth/Height × dpiScale`，按系统 DPI 标记 `DpiX/DpiY`，确保 1:1 物理像素光栅化。
3. **渲染根**：`rtb.Render(ContentRoot)` —— 仅图片 + 批注，天然排除 `AnnotationToolbar`（在 `ContentRoot` 外）与 `PinBorder`。
4. **输出**：`rtb` 即新 `BitmapSource`，`Freeze()` 后写入剪贴板 / `PngBitmapEncoder` 落盘。
