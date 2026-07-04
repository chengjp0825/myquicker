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

系统级架构契约与模块职责见 [`SPEC.md`](SPEC.md)。

## 开发约定
- 一次只实现一个模块（SPEC §5）；先验证 `StructLayout` 内存对齐再测钩子。
- 配置热重载：`MainWindow` 每次唤醒都经 `ActionStore` → `SettingsManager.Instance.Load()` 从磁盘重新加载动作列表；`Menu` 组经 `ApplyMenuSettings` 即时刷新，编辑 `settings.json` 无需重启。
- 注释风格：公开 API 附 XML 注释，关键约束在注释中标注对应 SPEC 节/步骤（如 `Per SPEC §4.1`）。
- 调试日志：用 `System.Diagnostics.Debug.WriteLine`（`[Conditional("DEBUG")]`，Debug 保留、Release 自动剥离）。**不要再加 `Console.WriteLine` 或 `AttachConsole`**（已全部移除）。

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
