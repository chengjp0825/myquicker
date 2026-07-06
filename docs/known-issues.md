# 已知问题（待修复）

本文件记录尚未解决的已知缺陷，供下次继续排查。修复后请将对应条目移除或标记为已修复并迁入对应 docs/ 规范。

## KI-1：副屏截图贴图与框选尺寸不一致（疑似等比放大到主屏 DPI）—— **已修复**

**报告**：2026-07-05。在缩放与主屏不同的副屏（尤其较小副屏）上截图后，贴图大小与实际画框大小不一致，表现为贴图被等比放大到主屏 DPI 尺寸。

**修复时间**：2026-07-06。

**根因**：`ScreenshotWindow` / `PinWindow` 均使用 `AllowsTransparency="True"`，在 .NET 8 WPF per-monitor DPI 下透明窗口常被强制为主屏 DPI；`TransformToDevice` 读取的是 WPF 渲染 DPI，非窗口实际所在显示器 DPI，导致两窗 scale 不一致、贴图尺寸与框选物理尺寸错位。

**修复方案**：
1. 在 `app.manifest` 中声明 `permonitorv2,permonitor,system` DPI awareness。
2. `ScreenshotWindow` 改为 `AllowsTransparency="False"`（覆盖层本就不透明，无需逐像素 alpha）。
3. 新增 `NativeMethods.GetDpiForWindow(hwnd)` 与 `DpiHelper.ScaleForWindow(hwnd)`，在 `SourceInitialized` / `DpiChanged` 中直接按 HWND 取确定性显示器 DPI，取代不稳定的 `TransformToDevice`。
4. `PinWindow` 保持 `AllowsTransparency="True"`（阴影需要 alpha），但 scale 来源同样改为 `GetDpiForWindow(hwnd)`。

**涉及文件**：`UI/ScreenshotWindow.xaml`、`UI/ScreenshotWindow.xaml.cs`、`UI/PinWindow.xaml.cs`、`Services/DpiHelper.cs`、`Interop/NativeMethods.cs`、`app.manifest`、`MyQuicker.csproj`。

**验证要点**：
- 混合 DPI 多显示器环境下，截图窗与贴图窗的 `renderScale` 日志值应等于目标显示器缩放系数。
- 贴图窗口的物理尺寸应与框选矩形的物理尺寸一致。
- 所有显示器缩放一致时行为与旧实现相同。
