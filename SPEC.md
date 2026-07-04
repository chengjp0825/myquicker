# Spec-Driven Development (SDD): MyQuicker Architecture

> 权威规范（PRD）。本文件描述系统必须满足的高层架构与行为契约；实现层面的约束与现状见 `docs/` 子系统规范与 `CLAUDE.md`。两层之间不重复——详细阈值、API 签名、算法步骤一律落在 `docs/`。

## 1. System Requirements & Stack
- Target Framework: .NET 8.0-windows
- UI Framework: WPF（`UseWPF`），辅以 WinForms（`UseWindowsForms`，仅用于 `Screen` / `NotifyIcon`）
- Native interop: P/Invoke 调用 `user32.dll` / `kernel32.dll` / `gdi32.dll` / `dwmapi.dll`
- Execution Context: Windows 10/11
- 配置持久化：`settings.json`（运行时由 `SettingsManager` 单例生成/读写，源码不纳管）；首次启动自动从旧 `appsettings.json` 迁移唤醒键与动作列表

## 2. Project Structure & Layering
严格三层职责分离，原生调用与 UI 渲染必须隔离。
- `/Interop` —— 底层 P-Invoke（`NativeMethods.cs`）。
- `/Services` —— 核心逻辑（无 UI 依赖）：`GlobalHookService` / `GestureHelper` / `ScreenshotService` / `ActionExecutor` / `SettingsManager` / `ActionStore`。
- `/UI` —— WPF 视图：`MainWindow` / `SettingsWindow` / `ScreenshotWindow` / `PinWindow` / `BrushHelper`。
- `/Models` —— 数据契约：`ActionItem` / `SettingsModel`（`ActionSettings` / `SnippingSettings` / `MenuSettings` / `PinSettings`）。
- `/Resources` —— 共享 XAML：`ThemeStyles.xaml`（由 `App.xaml` 合并）。

> 分层实现约束、各层职责细节与配置系统详见 [`docs/01-architecture-and-config.md`](docs/01-architecture-and-config.md)。

## 4. Core Module Logic

### 4.1 Global Mouse Hook Service (`GlobalHookService`)
全局低级鼠标钩子（`WH_MOUSE_LL`），监听光标按键与移动。回调内仅同步"吞键"，所有 UI 变化与磁盘 IO 异步派发；`WM_MOUSEMOVE` 永不拦截，作画圈识别旁观分支。唤醒方式可配置：中键 / 侧键后退 (XButton1) / 单纯画圈（无按键）。

> 钩子存活、异步分发时效、`WM_MOUSEMOVE` 旁观逻辑、P-Invoke 签名索引详见 [`docs/02-interaction-engine.md`](docs/02-interaction-engine.md)。

### 4.2 Frameless Wake-up Menu (`MainWindow`)
全局单例无框顶层菜单，预热后常驻（屏幕外 + 透明），唤醒仅瞬移定位 + 显透明度 + `SetWindowPos` 置顶不抢焦，零 IO。方块矩阵布局展示动作，光标位置居中，点击动作后睡眠。已唤醒时再次唤醒无效（防重入）。

> 极速唤醒渲染规范（单例预热 / 禁用 Show-Hide / 零 IO）详见 [`docs/03-ui-and-styling.md`](docs/03-ui-and-styling.md) §7；防重入闸判定细节见 [`docs/02-interaction-engine.md`](docs/02-interaction-engine.md)。

### 4.3 Action Execution Engine (`ActionExecutor`)
动作列表经 `ActionStore` 从 `settings.json` 加载。`sys:` 前缀为内部协议指令，由 `Execute` 拦截，不走 `Process.Start`；其余命令按外部进程启动。空命令校验 + `Process.Start` 容错。

> `sys:` 指令登记表与新增指引详见 [`docs/01-architecture-and-config.md`](docs/01-architecture-and-config.md)；容错细节见 [`docs/02-interaction-engine.md`](docs/02-interaction-engine.md)。

### 4.4 Configuration System (`SettingsManager` / `ActionStore`)
`SettingsManager` 全局单例，统一配置中心，读写 `SettingsModel` 四组（`Action` / `Snipping` / `Menu` / `Pin`）到 `settings.json`。`ActionStore` 静态门面持动作内存缓存（启动载入，唤醒零 IO，编辑深拷贝隔离）。`SettingsWindow` 4 页编辑全部四组字段（截屏与贴图合并页内分区），应用即落盘 + 刷新缓存 + 重绑菜单。

> 原子写、`.bak` 备份、旧配置迁移、统一配置三层（JSON / ThemeStyles / 内联）详见 [`docs/01-architecture-and-config.md`](docs/01-architecture-and-config.md)。

### 4.5 Circle Gesture Recognition (`GestureHelper`)
纯几何纯函数画圈判定，无状态、无副作用。调用方维护滑动时间窗，本方法只做空间几何判定（bounding box / 宽高比 / 转向数）。

> 判定阈值与算法步骤详见 [`docs/02-interaction-engine.md`](docs/02-interaction-engine.md)。

## 5. Execution Directives for LLM
- 一次只实现一个模块。
- 先验证 `StructLayout` 内存对齐再测钩子。
- 任何 P-Invoke 签名不得放进 UI code-behind，一律加到 `NativeMethods.cs`。
- 任何新增 GDI 对象遵循 `Freeze` 拷贝 → `DeleteObject` 释放（详见 `docs/02`）。
- 钩子回调内禁止做 IO 或 UI 变化，一律 `Dispatcher.BeginInvoke` 异步；`WM_MOUSEMOVE` 永不拦截。
- 公开 API 附 XML 注释，关键约束在注释中标注对应 SPEC 节。
- 调试日志用 `System.Diagnostics.Debug.WriteLine`（Release 自动剥离），不得 reintroduce `Console.WriteLine` / `AttachConsole`。
- 新增可配置项按统一配置三层决策树归位（详见 `docs/01`）。
