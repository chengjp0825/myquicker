# CLAUDE.md

MyQuicker —— 基于 WPF 的个人快捷启动器。鼠标唤醒触发器（中键 / 侧键 / 纯轨迹画圈）触发无框菜单，支持自定义动作与原生截屏。
Spec 驱动开发（SDD）：`SPEC.md` 为高层系统规范（PRD），`docs/` 为子系统详细规范。本文件仅作路由入口。

## 技术栈
- .NET 8.0-windows / WPF（`UseWPF`）+ WinForms（`UseWindowsForms`，仅用于 `Screen` / `NotifyIcon`）
- P/Invoke 调用 `user32.dll` / `kernel32.dll` / `gdi32.dll` / `dwmapi.dll`
- 配置持久化：`settings.json`（由 `SettingsManager` 单例运行时生成/读写，源码不纳管）；首次启动自动从旧 `appsettings.json` 迁移 `TriggerBinding`、命令与菜单配置

## 上下文检索指南（Context Router）

处理任务前，先按领域阅读对应 `docs/` 文件：

- 修改底层钩子 / 截屏坐标 / 触发器与手势轨迹算法 / GDI 回收 / 崩溃兜底 → 阅读 [`docs/02-interaction-engine.md`](docs/02-interaction-engine.md)
- 新增设置项 / 修改配置落盘逻辑 / 扩展 `sys:` 指令 / 三层架构调整 → 阅读 [`docs/01-architecture-and-config.md`](docs/01-architecture-and-config.md)
- 修改 UI 样式 / 增加新页面 / 调整颜色 / 菜单布局 / 截屏覆盖层与贴图视觉 → 阅读 [`docs/03-ui-and-styling.md`](docs/03-ui-and-styling.md)

未解决的已知缺陷见 [`docs/known-issues.md`](docs/known-issues.md)（截图 DPI 相关问题 KI-1 待排查，动手前先读）。

系统级架构契约与模块职责见 [`SPEC.md`](SPEC.md)。

## 开发约定
- 一次只实现一个模块（SPEC §5）；先验证 `StructLayout` 内存对齐再测钩子。
- 启动加载与零 IO 唤醒：`AppBootstrapper` 启动时通过 `SettingsManager` 加载 `Settings`，经 `BuiltInCommandProvider` / `UserCommandStore` 填充 `CommandRegistry`，经 `TriggerFactory` 构建运行时 `Trigger`，由 `MenuPresenter` 消费 `MenuGroup` 结构。唤醒路径上只允许内存访问，禁止磁盘 IO。
- 设置保存后的重建流程：`SettingsWindow` 保存时，先由 `SettingsManager` 写入 `Settings`，再按上述启动链路重建运行时对象（Factory/Builder 单向构建），最后通知 `MenuPresenter` 刷新。不得直接修改已序列化的运行时对象。
- 窗口生命周期：`MenuPresenter` 管理的 WPF 窗口为全局单例预热常驻，显隐走屏幕外瞬移 + `Opacity`（禁用 `Show`/`Hide`/`Visibility`），见 docs/03 §7。
- 注释风格：公开 API 附 XML 注释，关键约束在注释中标注对应 SPEC 节/步骤（如 `Per SPEC §4.1`）。
- 调试日志：一律用 `System.Diagnostics.Debug.WriteLine`（`[Conditional("DEBUG")]`，Debug 保留、Release 自动剥离）。**DEBUG 配置下必须能在 `dotnet watch run` / `dotnet run` 的终端看到日志输出**——机制：`<OutputType>` Debug=`Exe`（控制台子系统）/ Release=`WinExe`，`App.OnStartup` 在 `#if DEBUG` 注册 `ConsoleTraceListener`（→ stdout，终端可见）+ `TextWriterTraceListener`（→ exe 同目录 `debug.log`，可 `tail -f`），`Trace.AutoFlush=true`。新增调试日志继续用 `Debug.WriteLine`（由 ConsoleTraceListener 统一桥接到终端），**不要直接 `Console.WriteLine` 或 `AttachConsole`**。

## Git Rules

### Commit Format

```text
<type>(<scope>): <description>
```

### Rules

- 一次 commit 只做一件逻辑变更
- 禁止混合 feature / fix / refactor
- 禁止使用模糊提交（update / fix bug / test）
- 禁止提交信息携带 Agent、AI信息

## 开发环境备注

- **API = `glm-latest` 时命令安全分类器不可用**：Claude Code 的 auto 模式无法判定写操作安全性，会阻塞 `git commit` / `git add` / `git checkout` 等变更类命令（报 `auto mode cannot determine the safety`），只读工具（Read / Grep / Glob / `git diff` / `git status` / `git log`）不受影响。遇此情形：让用户手动批准该次工具调用，或切回可用 API（如 Claude 官方模型）后再提交；不要在分类器不可用时反复重试同一写命令。

## 开发指令

标准构建与测试命令：

```powershell
# 构建整个解决方案
dotnet build

# 运行测试（如有测试项目）
dotnet test

# Debug 下调试运行（确保终端日志可见）
dotnet run --configuration Debug

# 或热重载开发
dotnet watch run --configuration Debug
```

Release 构建产物为 `WinExe`（无控制台窗口），Debug 构建产物为 `Exe`（保留控制台窗口用于日志输出）。

## 架构核心原则

1. **顶层唤醒概念一律为 `Trigger`**
   - `Trigger` 是任何唤醒菜单的用户动作的顶层总称。
   - `Trigger.Evaluate()` 统一返回 `TriggerMatchResult`；匹配成功时携带 `WakeContext`（位置、时间戳、触发源）。
   - `ButtonTrigger` 用于瞬间硬件输入（鼠标中键、侧键、键盘快捷键等）。
   - `GestureTrigger` / `PathTrigger` 仅用于需要评估时间和空间轨迹的指针运动（如画圆）。
   - 严禁将静态点击、按键等瞬时输入混称为 `Gesture`。
   - 完整术语表与决策依据见 [`CONTEXT.md`](CONTEXT.md) 与 [`docs/adr/0001-trigger-as-umbrella-concept.md`](docs/adr/0001-trigger-as-umbrella-concept.md)。

2. **Settings DTO 与运行时对象物理隔离**
   - `Settings` 是纯数据对象，仅用于持久化（`settings.json`），不得持有服务引用、窗口句柄、钩子或其他运行时状态。
   - 启动构建链（必须严格按此顺序执行）：
     1. `SettingsManager` 加载并迁移 `Settings`。
     2. `BuiltInCommandProvider` 注册不可变系统命令。
     3. `UserCommandStore` 从 `Settings.Commands` 注册用户命令。
     4. `TriggerFactory` 从 `Settings.TriggerBindings` 构建运行时 `Trigger`。
     5. `MenuPresenter` 消费 `Settings.MenuGroups`。
   - `Trigger`、`Command`、`Menu` 等运行时对象必须通过上述工厂/构建器从 `Settings` **单向构建**，不得直接序列化。
   - 保存设置后，必须重新走完整 Factory/Builder 链路重建运行时对象，禁止就地修补已构建对象。
   - 决策依据见 [`docs/adr/0002-separate-settings-dto-from-runtime-objects.md`](docs/adr/0002-separate-settings-dto-from-runtime-objects.md)。

3. **策略层与表现层严格解耦**
   - `WakeOrchestrator` 拥有唤醒策略、菜单生命周期状态机、防抖、多显示器/DPI 边界检查、前台应用过滤。它只输出 `ShowAt(Location)` / `Dismiss()` 等 UI 框架无关命令。
   - `MenuPresenter` 负责 WPF 窗口、动画、Z-Order、命中测试与动作布局渲染。它只接收命令，不参与策略决策。
   - `WakeOrchestrator`、`Command` 派生类、`Trigger` 派生类、`ScreenshotCaptureService` 等核心领域/策略组件中，**不得引用 `System.Windows` 或任何 WPF 相关类型**。
   - 所有与 WPF 相关的代码必须隔离在 `MenuPresenter`、`ScreenshotOverlay`、`ScreenshotPinService` 等显式表现层组件中。

4. **Action 与 Command 分离，Command 通过 `CommandContext` 方法注入**
   - `Action` 是菜单中的视觉配置节点（Id、DisplayName、Icon、CommandId），不包含执行逻辑。
   - `Command` 是无状态可执行载荷，以密封类层次实现（`LaunchApplicationCommand`、`OpenUrlCommand`、`RunScriptCommand`、`SystemCommand` 等）。
   - `Command` 通过 `Execute(CommandContext)` 接收运行时依赖（`ProcessLauncher`、`WindowManager`、`SettingsService` 等），禁止在构造函数中注入服务或在字段中持有服务引用。
   - `CommandRegistry` 提供 O(1) ID 查找；`BuiltInCommandProvider` 与 `UserCommandStore` 分别注册系统命令与用户命令。

5. **截图子域独立**
   - `ScreenshotCaptureService`、`ScreenshotOverlay`、`ScreenshotPinService` 组成独立子域，不得与菜单核心逻辑耦合。
   - `sys:screenshot` 仅作为工作流编排命令，按 Capture → Select Region → Pin 的顺序调用子域服务。

## 命名与语义规范

- **顶层总称一律使用 `Trigger`**，严禁将静态点击（如鼠标中键、侧键、键盘快捷键）混称为 `Gesture`。
  - `GestureTrigger` / `PathTrigger` 仅用于需要评估时间和空间轨迹的指针运动（如画圆）。
  - `ButtonTrigger` 用于瞬间的硬件输入动作。
- `Action` 指菜单中的视觉配置节点；`Command` 指可执行逻辑载荷。二者不得混用。
- `WakeContext` 携带唤醒时的位置、时间戳与触发源，是输入层与表现层之间的唯一解耦契约。

## Agent skills

### Issue tracker

Issues are tracked through natural-language prompts/conversations in this repo, not in a formal issue tracker. See `docs/agents/issue-tracker.md`.

### Triage labels

Uses the default canonical labels: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context layout: one `CONTEXT.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.

