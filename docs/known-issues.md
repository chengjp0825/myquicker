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

**涉及文件**：`UI/ScreenshotWindow.xaml`、`UI/ScreenshotWindow.xaml.cs`、`UI/PinWindow.xaml.cs`、`Services/DpiHelper.cs`、`Interop/NativeMethods.cs`、`app.manifest`、`aurora.csproj`。

**验证要点**：
- 混合 DPI 多显示器环境下，截图窗与贴图窗的 `renderScale` 日志值应等于目标显示器缩放系数。
- 贴图窗口的物理尺寸应与框选矩形的物理尺寸一致。
- 所有显示器缩放一致时行为与旧实现相同。

---

以下条目（KI-2 起）为 2026-07-07 对抗式审查（业务逻辑轴 + 用户交互轴）发现的未解决缺陷，按严重度排序。修复后请将对应条目标记为已修复或移除。

## KI-2：画圈唤醒在生产环境完全失效（EventReceived 未订阅）—— **已修复**

- **严重度**：Critical
- **报告**：2026-07-07（对抗式审查 · 业务轴 C-1）
- **修复时间**：2026-07-07。
- **现象**：配置“画圈唤醒”后，画圈永远打不开菜单。`WM_MOUSEMOVE` 经 `EventReceived` 路由，但无任何代码订阅该事件，MouseMove 永不到达 `CircleGestureTrigger`。单元测试因隔离测试而通过。
- **根因**：计划 `docs/superpowers/plans/2026-07-06-deepen-architecture-seams.md` Phase 4 Step 2 要求的 `RawInputSource.EventReceived += (s,ev) => TriggerEvaluator.Evaluate(ev)...` 接线从未实现；`App.xaml.cs` 仅订阅 `WakeContextReceived` 与 `AnyMouseDown`。
- **修复方案**：顺“内部评估”路线（与 `MouseDown` 路径一致）为 `WM_MOUSEMOVE` 补评估——把 `HookCallback` 的消息派发提取为 `internal ProcessMouseMessage`，`WM_MOUSEMOVE` 分支同步调新增的 `EvaluateAndPostWake`（评估→匹配则投递 `WakeContextReceived`）；`EvaluateAndMaybeSwallow` 复用 `EvaluateAndPostWake`；`PostEvent` 加 `EventReceived is null` 短路避免高频无用闭包。重构严格等价于原 `MouseDown` 行为。补 `RawInputSourceTests`（6 例）覆盖画圈唤醒、直线不匹配、`EventReceived` seam、无订阅短路、`AnyMouseDown`、吞键。
- **涉及文件**：`src/Domain/Runtime/RawInputSource.cs`、`tests/aurora.Tests/Domain/Runtime/RawInputSourceTests.cs`。
- **验证要点**：
  - `dotnet test` 全绿（154 通过，含新增 6 例）。
  - 手动：配置画圈触发器后画圈应能打开菜单（需退出当前运行中的 aurora 实例后重新运行确认）。
- **状态**：已修复

## KI-3：低级鼠标钩子回调无异常保护，任意异常杀死全局钩子 —— 已修复

- **严重度**：High
- **报告**：2026-07-07（对抗式审查 · 业务轴 H-1）
- **涉及**：`src/Domain/Runtime/RawInputSource.cs:110-159`
- **现象**：`WH_MOUSE_LL` 回调跑在 UI 线程消息循环；`HookCallback` 无 try/catch，`Marshal.PtrToStructure` / 同步 `Evaluate` / `_sync.Post` 任一抛异常都会冒出原生回调，Windows 移除低级钩子或 CLR 终止，全部唤醒输入静默死亡、无日志、无恢复。`App_DispatcherUnhandledException` 对非安全异常 `e.Handled=false` 仍崩溃。
- **修复方向**：`nCode >= 0` 主体包 try/catch，`Debug.WriteLine` 后始终落到 `CallNextHookEx`。
- **修复时间**：2026-07-07。
- **状态**：已修复（`dotnet test` 154 通过）

## KI-4：损坏 settings.json 恢复返回空 Settings，唤醒瘫痪 —— 已修复

- **严重度**：High
- **报告**：2026-07-07（对抗式审查 · 业务轴 H-2）
- **涉及**：`Services/SettingsManager.cs:198-206,210-226`
- **现象**：`JsonException` → `BackupCorruptFile` → `return new Settings()`，`TriggerBindings`/`MenuGroups` 均空；`Load()` 此路径不调 `CreateDefaultSettings()`。损坏后首次启动零触发器+空菜单，违反“默认常驻动作”契约。
- **修复方向**：`JsonException` 分支返回 `CreateDefaultSettings()`（或至少种入默认触发器）。
- **修复时间**：2026-07-07。
- **状态**：已修复（`dotnet test` 154 通过）

## KI-5：内建 sys:* 命令 ID 可被用户配置覆盖 —— 已修复

- **严重度**：High
- **报告**：2026-07-07（对抗式审查 · 业务轴 H-3）
- **涉及**：`Services/UserCommandStore.cs:14-32`、`src/Domain/Runtime/Commands/CommandRegistry.cs:17-25`
- **现象**：`CommandRegistry.Register` 直接 `_commands[key]=command`（静默覆盖）；`UserCommandStore` 无 `sys:` 前缀过滤。用户 `Id="sys:snipping"` `Target="evil.exe"` 会替换内建 `SnippingCommand`。违反 `CONTEXT.md` “拒绝覆盖内建命令 ID”。
- **修复方向**：`UserCommandStore.Register` 跳过 `sys:` 前缀项；或 `CommandRegistry` 在内建注册后拒绝 `sys:` 键。
- **修复时间**：2026-07-07。
- **状态**：已修复（采用前者：`UserCommandStore.Register` 跳过 `sys:` 前缀项并 `Debug.WriteLine` 记日志；`dotnet test` 154 通过）

## KI-6：ScreenshotCaptureService 捕获失败时 Bitmap 泄漏 GDI+ 句柄 —— 待修复

- **严重度**：Medium
- **报告**：2026-07-07（对抗式审查 · 业务轴 M-1）
- **涉及**：`Services/ScreenshotCaptureService.cs:44-55`
- **现象**：`using` 仅包 `Graphics`，不包 `Bitmap`；`CopyFromScreen` 抛异常时 `Bitmap` 未释放。
- **修复方向**：`Bitmap` 包 `using` 或 try/finally 失败路径释放。
- **状态**：待修复

## KI-7：SnippingCommand.RunAsync 火-忘无异常处理 —— 待修复

- **严重度**：Medium
- **报告**：2026-07-07（对抗式审查 · 业务轴 M-2）
- **涉及**：`src/Domain/Runtime/Commands/SnippingCommand.cs:19`、`src/Domain/Runtime/ScreenshotWorkflow.cs:38-97`
- **现象**：`_ = context.ScreenshotWorkflow.RunAsync()` 无 try/catch；overlay/pin 抛异常成为未观察任务异常，无用户反馈。计划 Phase 6 Task 6.3 要求的 try/catch+toast 未实现。
- **修复方向**：包 try/catch，失败 `context.ToastService?.Show(...)`。
- **状态**：待修复

## KI-8：输入拦截策略用 .All 汇总，破坏 per-trigger 拦截 —— 待修复

- **严重度**：Medium
- **报告**：2026-07-07（对抗式审查 · 业务轴 M-3）
- **涉及**：`src/AppBootstrapper.cs:130`、`src/Services/InputInterceptionPolicy.cs`
- **现象**：`interceptWakeupKey = TriggerBindings.All(b => b.InterceptWakeupKey)`；空表返回 true，任一 false 则全局不拦截，与该绑定 `InterceptWakeupKey=true` 意图矛盾。
- **修复方向**：`IInputInterceptionPolicy.ShouldIntercept(WakeContext)` 查匹配绑定，或建 per-trigger 策略表。
- **状态**：待修复

## KI-9：ScreenshotWorkflow 取消令牌未下传，中途不可取消 —— 待修复

- **严重度**：Medium
- **报告**：2026-07-07（对抗式审查 · 业务轴 M-4）
- **涉及**：`src/Domain/Runtime/ScreenshotWorkflow.cs:38-97`
- **现象**：token 仅步边界检查；`CaptureAsync`/`SelectRegionAsync`/`PinAsync` 不收 token；`SnippingCommand` 用默认 token。仅 overlay ESC 可取消。
- **修复方向**：token 穿透子域接口，或文档化为 overlay-ESC-only。
- **状态**：待修复

## KI-10：SettingsBuilder 把 triggerBinding 按引用存入，DTO 隔离被打破 —— 待修复

- **严重度**：Medium
- **报告**：2026-07-07（对抗式审查 · 业务轴 M-5）
- **涉及**：`src/Services/SettingsBuilder.cs:53-55`
- **现象**：`Build` 克隆了 snipping/menu/pin，但 `TriggerBindings` 直接存传入引用；保存后 `SettingsManager.Settings.TriggerBindings[0]` 即 view-model 实例，用户继续编辑会活改持久化 Settings，违反 ADR-0002。
- **修复方向**：`Build` 深拷贝 triggerBinding（仿 `SettingsViewModel.Copy`）。
- **状态**：待修复

## KI-11：陈旧事件阈值 1s 偏紧，UI 卡顿时丢合法唤醒 —— 待修复

- **严重度**：Medium
- **报告**：2026-07-07（对抗式审查 · 业务轴 M-6）
- **涉及**：`src/Domain/Runtime/WakeOrchestrator.cs:100-103`
- **现象**：`Timestamp` 在 `HookCallback` 捕获，`now` 在 posted 回调捕获；dispatcher 忙 >1s 即被当陈旧丢弃。
- **修复方向**：阈值提到 ~2-3s，或在 post 时捕获 `now` 传入。
- **状态**：待修复

## KI-12：唤醒菜单无键盘处理（Esc/Enter/方向键）—— 待修复

- **严重度**：High
- **报告**：2026-07-07（对抗式审查 · UX 轴 H-1）
- **涉及**：`UI/MainWindow.xaml.cs`（整文件）、`UI/MainWindow.xaml`
- **现象**：无 `KeyDown`/`PreviewKeyDown`/`Esc` 处理器；菜单只能点动作/外部/齿轮关闭。窗口 `WS_EX_NOACTIVATE` 不获键盘焦点，`PreviewKeyDown` 可能不触发。
- **修复方向**：`MainWindow` 加 `PreviewKeyDown`：Esc→`RaiseDismissRequested()`、Enter→激活聚焦按钮、可选方向键；唤醒时给临时焦点或挂键盘钩子。
- **状态**：待修复

## KI-13：Alt-Tab/失活不关闭菜单，topmost 菜单滞留新前台之上 —— 待修复

- **严重度**：High
- **报告**：2026-07-07（对抗式审查 · UX 轴 H-2）
- **涉及**：`UI/MainWindow.xaml.cs`
- **现象**：无 `Deactivated` 处理；Alt-Tab/Win+D/点任务栏不产生 hook 能及时识别为“外部”的 mousedown，topmost 菜单一直盖新前台窗口。
- **修复方向**：订阅 `Deactivated` → `RaiseDismissRequested()`。
- **状态**：待修复

## KI-14：菜单内容相对光标偏移 12px，贴边时最后 12px 被裁 —— 待修复

- **严重度**：High
- **报告**：2026-07-07（对抗式审查 · UX 轴 H-3）
- **涉及**：`src/Domain/Runtime/WakeOrchestrator.cs:59,142`、`UI/MainWindow.xaml:27-28`、`UI/MainWindow.xaml.cs:187-188`
- **现象**：`ClampToScreen` 用 `MenuWidth`（内容宽）算 `left=dipX-halfW`，但 `MainWindow.Width=menu.Width+24` 且 `RootBorder Margin=12`——窗口左边在内容左 12px，内容中心=`dipX+12`；且 `left+MenuWidth<=screenRight` 保证内容右边可达 `screenRight+12`，贴右/下边唤醒时最后一列/底栏被裁 12px。
- **修复方向**：`WakeUp` 设 `Left=location.X-12`，或 `ClampToScreen` 用 `MenuWidth+24`/`MenuHeight+24` 并偏移 12。
- **状态**：待修复

## KI-15：ScreenshotWindow.OnMouseMove 无异常保护，放大镜路径异常崩溃应用 —— 待修复

- **严重度**：High
- **报告**：2026-07-07（对抗式审查 · UX 轴 H-4）
- **涉及**：`UI/ScreenshotWindow.xaml.cs:280-360`
- **现象**：`UpdateMagnifierLoupe` 每次 mousemove 分配 `CroppedBitmap`+`CopyPixels`；`_baseImage` 状态异常（`OnClosed` 竞态）抛异常 → `App.DispatcherUnhandledException` `e.Handled=false` → 进程终止，`ClipCursor` 仍激活。
- **修复方向**：放大镜主体包 try/catch，降级隐藏 loupe。
- **状态**：待修复

## KI-16：菜单 Opening/Visible/Closing 期间二次唤醒被静默忽略 —— 待修复

- **严重度**：High
- **报告**：2026-07-07（对抗式审查 · UX 轴 H-5）
- **涉及**：`src/Domain/Runtime/WakeOrchestrator.cs:59`
- **现象**：`OnWakeContext` 在 `State!=Hidden` 时直接 return；开启 150ms+关闭 120ms=270ms 内新位置二次触发无反馈。双击中键或“以为没反应”再触发什么也得不到。
- **修复方向**：`Visible` 态允许 `Dismiss()` 后在新位置重显，或直接 `ShowAt` 重锚。
- **状态**：待修复（注：`WakeOrchestratorTests` 断言“可见态忽略二次唤醒”为有意；本条记录该设计的 UX 缺陷，需产品决策是否调整。）

## KI-17：异步保存期间手动关窗跳过运行时重建 —— 待修复

- **严重度**：Medium
- **报告**：2026-07-07（对抗式审查 · UX 轴 M-1）
- **涉及**：`UI/SettingsWindow.xaml.cs:157-247`
- **现象**：`SaveAndCloseAsync` 后台 await `SaveAsync` 后 `Dispatcher.InvokeAsync` 触发 `SettingsSaved`；若用户在保存期间关窗，`OnClosed` 置 `SettingsSaved=null`，保存完成时空操作，`RebuildRuntime` 不跑，磁盘已写但活应用仍用旧配置。
- **修复方向**：保留 handler 本地副本，或 `IsClosed` 检查后补触发重建。
- **状态**：待修复

## KI-18：PinWindow 文本编辑器激活时“清除批注”生成多余 TextBlock —— 待修复

- **严重度**：Medium
- **报告**：2026-07-07（对抗式审查 · UX 轴 M-2）
- **涉及**：`UI/PinWindow.xaml.cs:529-545`
- **现象**：`ClearAnnotations_Click` 先 `Children.Clear()` 再 `ExecuteEdit`；若 TextBox 编辑器激活，其 `LostFocus`(`CommitTextEditor`) 在 clear 后触发，`IndexOf(tb)=-1`，最终 `Children.Add(blk)` 把 TextBlock 加回空 canvas。
- **修复方向**：`CommitTextEditor` 在 `tb.Tag` null 或 tb 已不在 `Children` 时 bail。
- **状态**：待修复

## KI-19：Toast 永远定位主屏，多屏用户看错屏 —— 待修复

- **严重度**：Medium
- **报告**：2026-07-07（对抗式审查 · UX 轴 M-3）
- **涉及**：`UI/ToastWindow.xaml.cs:32-46`
- **现象**：`SystemParameters.WorkArea` 是主屏工作区；副屏用户 toast 出现在主屏。
- **修复方向**：按光标所在显示器（`Cursor.Position`→`Screen.FromPoint`）定位。
- **状态**：待修复

## KI-20：ExecuteActionAsync 等关闭的 TCS 无超时，可能锁死后续动作点击 —— 待修复

- **严重度**：Medium
- **报告**：2026-07-07（对抗式审查 · UX 轴 M-4）
- **涉及**：`UI/MainWindow.xaml.cs:274-292`
- **现象**：TCS 键 `_closed` 事件；若关闭故事板 `Completed` 不触发（关闭中再 `WakeUp` 取消故事板），`_closed` 不 raise，TCS 不完成，`_actionExecutionInProgress` 永真。
- **修复方向**：`WaitForMenuClosedAsync` 加超时（如 500ms）强制完成。
- **状态**：待修复

## KI-21：PinWindow 批注模式不阻止唤醒 —— 待修复

- **严重度**：Medium
- **报告**：2026-07-07（对抗式审查 · UX 轴 M-5）
- **涉及**：`src/Services/OverlayWakeBlockPolicy.cs:16-19`
- **现象**：`IsBlocked` 只查 `ScreenshotWindow`/`SettingsWindow`；批注模式类模态，中键唤醒仍弹菜单。
- **修复方向**：任一 `PinWindow` `_annotationModeEnabled==true` 时也阻止。
- **状态**：待修复

## KI-22：PinWindow _toolbarHideTimer 关窗未停 —— 待修复

- **严重度**：Medium
- **报告**：2026-07-07（对抗式审查 · UX 轴 M-6）
- **涉及**：`UI/PinWindow.xaml.cs:253-261`
- **现象**：`DispatcherTimer` `OnClosed` 未 `Stop`；关窗 300ms 后仍可能 `FadeOutToolbar` → 对分离元素 `BeginAnimation`。
- **修复方向**：`OnClosed` 里 `_toolbarHideTimer.Stop()`。
- **状态**：待修复

## KI-23：ClipCursor 整个截图会话锁光标于捕获边界 —— 待修复

- **严重度**：Medium
- **报告**：2026-07-07（对抗式审查 · UX 轴 M-7）
- **涉及**：`UI/ScreenshotWindow.xaml.cs:125-133`
- **现象**：`CaptureScope=CurrentMonitor` 时用户无法移光标到另一屏；`OnSourceInitialized` 在 `ClipCursor` 之后抛异常则光标停留裁剪。
- **修复方向**：`ClipCursor` 与 `OnClosed` 释放配对 finally，或仅拖拽期间裁剪。
- **状态**：待修复

## KI-24：双击启动瞬间全局鼠标卡死（低级钩子挂载过早）—— **已修复**

- **严重度**：Critical
- **报告**：2026-07-07（本地实测）
- **修复时间**：2026-07-07。
- **现象**：双击启动应用瞬间，鼠标产生肉眼可见的全局短时间卡死。
- **根因**：`App.OnStartup` 在 `MainWindow.Show()` 之后、`InitializeTray()` 之前同步调用 `RawInputSource.Start()` 挂载 `WH_MOUSE_LL` 全局钩子。钩子挂载后，`InitializeTray()`（`Shell_NotifyIcon(NIM_ADD)` 与 explorer 同步通信）与 `Toast.Show()`（`new ToastWindow().Show()` WPF 渲染）仍在主线程同步执行，阻塞消息循环；`WH_MOUSE_LL` 回调在安装线程消息循环里 dispatch，主线程阻塞导致回调排队超时（`LowLevelHooksTimeout` 默认 300ms），鼠标消息挂起 → 全局卡死。
- **修复方案**：把 `RawInputSource.Start()` 物理后移到 `InitializeTray()` + `Toast.Show()` 之后（启动收尾末步），并用 `Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => _bootstrapper.RawInputSource.Start()), DispatcherPriority.Background)` 包裹，强制推迟到下一帧消息循环 Pump 完托盘与 Toast 渲染积压消息、主线程回归静默后再激活钩子。约定已固化至 `CLAUDE.md`「低级钩子挂载时序」。
- **涉及文件**：`App.xaml.cs`。
- **验证要点**：
  - `dotnet build -c Release` 0 警告 0 错误；`dotnet test` 154 通过。
  - 手动：双击 Release exe 冷启动，鼠标丝滑无卡顿。
- **状态**：已修复

## 低优先级清理清单（审查 Low，未单独编号）

- **业务 L-1**：`Services/ActionResult.cs` + `UI/MainWindow.xaml.cs:319-323` — `ActionResult.CapturedImage` 死字段+潜在 GDI 泄漏陷阱；消费方释放或删字段。
- **业务 L-2**：`src/Domain/Runtime/CircleGestureTrigger.cs:23` — 构造器收 `ITimeProvider` 却从不使用；删参或用之。
- **业务 L-3**：`src/Domain/Runtime/Point.cs` + `WakeOrchestrator.cs:142` — `Point` 文档称“物理坐标点”实返 DIP；改文档或引入 Physical/DIP 类型。
- **业务 L-4**：`UI/MainWindow.xaml.cs:377-403` — 关闭故事板显式 `From=1`，开启中途 dismiss 闪一下；关闭前清开启故事板或不用 `From`。
- **业务 L-5**：`src/Domain/Runtime/Commands/LaunchApplicationCommand.cs:34-46` — 绝对路径绕过 PATH 白名单（含 `powershell -EncodedCommand`）；文档澄清威胁模型。
- **UX L-1**：`src/Domain/Runtime/WakeOrchestrator.cs:142` — `ClampToScreen` 截断 DIP 为 int，150% DPI 最多偏 1px。
- **UX L-2**：`UI/PinWindow.xaml.cs:281-297` — `WM_DPICHANGED` 处理触发良性反馈循环，跨屏拖动冗余。
- **UX L-3**：`UI/MainWindow.xaml.cs:206-208` — `SetWindowPos(HWND_TOPMOST)` 每次 wake 调但 dismiss 不清；隐藏菜单仍 topmost。
- **UX L-4**：`UI/MainWindow.xaml.cs:298-313` — `WaitForMenuClosedAsync` 订阅 `_closed` 未在他路径完成时退订（今日安全，防御性）。
