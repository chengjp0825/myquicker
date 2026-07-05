# 已知问题（待修复）

本文件记录尚未解决的已知缺陷，供下次继续排查。修复后请将对应条目移除或标记为已修复并迁入对应 docs/ 规范。

## KI-1：副屏截图贴图与框选尺寸不一致（疑似等比放大到主屏 DPI）

**报告**：2026-07-05。在缩放与主屏不同的副屏（尤其较小副屏）上截图后，贴图大小与实际画框大小不一致，表现为贴图被等比放大到主屏 DPI 尺寸。

**涉及文件**：`UI/ScreenshotWindow.xaml.cs`、`UI/PinWindow.xaml.cs`、`Services/ScreenshotService.cs`、`Services/DpiHelper.cs`、`Interop/NativeMethods.cs`。详见 `docs/02-interaction-engine.md §5`。

**已做的修复尝试**（均未根治）：
1. `ScreenshotService.ComputeBounds`：`AllMonitors` 在混合 DPI 时回退当前屏 + toast。
2. `DpiHelper`：新增 `ScaleForBounds`（`GetDpiForMonitor`）与 `AllScreensSameScale`。
3. `ScreenshotWindow`：`SourceInitialized` 用 `SetWindowPos` 物理坐标强制定位 + `CompositionTarget.TransformToDevice`（实际渲染 DPI）修正 `_scaleX/Y`，`DpiChanged` 重算。裁剪 `selection × scale`、寻边 `(rect - bounds) / scale`。
4. `PinWindow`：`ReapplyMetrics` 用自身 `TransformToDevice` 重算 `PinImage` 尺寸/定位，`SourceInitialized`/`DpiChanged` 调用。

**理论分析**（为何应正确）：裁剪为物理像素，`PinImage.Width = 物理像素 / actualScale` + `Stretch=Fill` → 渲染 `× actualScale = 物理像素 1:1`；框选物理尺寸 = 裁剪物理尺寸，故贴图应恒等于框选。但实测仍不一致，说明某处假设不成立。

**下次排查步骤**（先修好终端日志，见 CLAUDE.md「调试日志」）：
1. `dotnet watch run`，在副屏复现，观察终端 `debug.log`：
   - `DEBUG: ScreenshotWindow bounds=... scale=(...)` —— 构造时 `GetDpiForMonitor` 估值。
   - `DEBUG: ApplyRenderMetrics renderScale=(...) physBounds=...` —— 截图窗**实际渲染 DPI**。
   - `DEBUG: EdgeDetect -> hwnd=... physRect=... dipSel=...` —— 寻边物理矩形与 DIP 选区。
   - `DEBUG: Capture Rect - x=.. y=.. w=.. h=..` —— 裁剪物理矩形。
   - `DEBUG: PinWindow ReapplyMetrics renderScale=(...) natural=.. screen=.. border=..` —— 贴图窗**实际渲染 DPI**。
2. 关键对比：
   - 截图窗 `renderScale` 与贴图窗 `renderScale` 是否一致？若不一致 → 两窗被赋不同 DPI（`AllowsTransparency=True` 的 per-monitor 行为不稳定）。
   - 截图窗 `renderScale` 是否等于副屏真实 DPI？若等于主屏 DPI → `SetWindowPos` 未把 HWND 移到副屏，或 `AllowsTransparency` 强制主屏 DPI。
   - `Capture Rect` 的 w/h 与用户画框的物理尺寸是否一致？

**待验证假设**：
- (a) `AllowsTransparency=True` 窗口在 .NET 8 WPF 下不获得 per-monitor DPI，`TransformToDevice` 恒为主屏。若如此，两窗应同 DPI（都主屏），贴图应正确——但实测不对，需日志确认两窗 `renderScale` 实际值。
- (b) `SourceInitialized` 读 `TransformToDevice` 时 `WM_DPICHANGED` 尚未处理，读到旧（主屏）DPI；`DpiChanged` 对透明窗可能不触发，导致不修正。
- (c) `SetWindowPos` 未实际改变 HWND 所在显示器（被 WPF 布局覆盖回原位）。
- (d) `e.GetPosition(this)` 给出的 DIP 与 `selection × scale` 的物理换算在某环节失配（如 `DragThreshold` 或 `ApplySelection` 的坐标空间）。

**可能的根治方向**（择一验证）：
- 将 `ScreenshotWindow` 改为 `AllowsTransparency=False`（覆盖层本就不透明，无需逐像素 alpha），获取稳定 per-monitor DPI；`PinWindow` 同理评估。
- 或在 `SourceInitialized` 后用 `GetDpiForWindow(hwnd)` 取 HWND 所在屏 DPI（确定性，不依赖 WPF 异步 DPI 更新），并显式处理 `WM_DPICHANGED`。
- 或在 `Loaded`（而非 `SourceInitialized`）重算尺寸（DPI 已稳定），接受可能的首次渲染闪烁。

**不要**在未取得上述日志前继续盲改——先确认两窗 `renderScale` 实际值，再定方向。
