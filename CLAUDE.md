# CLAUDE.md

MyQuicker —— 基于 WPF 的个人快捷启动器。鼠标唤醒手势（中键 / 侧键 / 纯轨迹画圈）触发无框菜单，支持自定义动作与原生截屏。
Spec 驱动开发（SDD）：`SPEC.md` 为高层系统规范（PRD），`docs/` 为子系统详细规范。本文件仅作路由入口。

## 技术栈
- .NET 8.0-windows / WPF（`UseWPF`）+ WinForms（`UseWindowsForms`，仅用于 `Screen` / `NotifyIcon`）
- P/Invoke 调用 `user32.dll` / `kernel32.dll` / `gdi32.dll` / `dwmapi.dll`
- 配置持久化：`settings.json`（由 `SettingsManager` 单例运行时生成/读写，源码不纳管）；首次启动自动从旧 `appsettings.json` 迁移唤醒键与动作列表

## 上下文检索指南（Context Router）

处理任务前，先按领域阅读对应 `docs/` 文件：

- 修改底层钩子 / 截屏坐标 / 手势算法 / GDI 回收 / 崩溃兜底 → 阅读 [`docs/02-interaction-engine.md`](docs/02-interaction-engine.md)
- 新增设置项 / 修改配置落盘逻辑 / 扩展 `sys:` 指令 / 三层架构调整 → 阅读 [`docs/01-architecture-and-config.md`](docs/01-architecture-and-config.md)
- 修改 UI 样式 / 增加新页面 / 调整颜色 / 菜单布局 / 截屏覆盖层与贴图视觉 → 阅读 [`docs/03-ui-and-styling.md`](docs/03-ui-and-styling.md)

未解决的已知缺陷见 [`docs/known-issues.md`](docs/known-issues.md)（截图 DPI 相关问题 KI-1 待排查，动手前先读）。

系统级架构契约与模块职责见 [`SPEC.md`](SPEC.md)。

## 开发约定
- 一次只实现一个模块（SPEC §5）；先验证 `StructLayout` 内存对齐再测钩子。
- 动作缓存：`ActionStore` 启动时一次性载入动作列表到内存，唤醒零 IO；`SettingsWindow` 保存时 `UpdateCache` 同步缓存 + `MainWindow.RefreshActions` 重绑菜单。`Menu` 组经 `ApplyMenuSettings` 即时刷新。MainWindow 为全局单例预热常驻，显隐走屏幕外瞬移 + `Opacity`（禁用 `Show`/`Hide`/`Visibility`），见 docs/03 §7。
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

## Agent skills

### Issue tracker

Issues are tracked through natural-language prompts/conversations in this repo, not in a formal issue tracker. See `docs/agents/issue-tracker.md`.

### Triage labels

Uses the default canonical labels: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context layout: one `CONTEXT.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.

