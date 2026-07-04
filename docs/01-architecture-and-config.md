# 架构与配置规范（Architecture & Configuration）

> 子系统详细规范。展开 `SPEC.md` §2 / §4 中架构分层与配置系统的实现约束。
> 修改配置落盘逻辑、新增设置项、扩展 `sys:` 指令或调整三层架构时阅读本文件。

## 1. 三层职责分离

严格三层职责分离，原生调用与 UI 渲染必须隔离（`SPEC.md` §2）。

### `/Interop` —— 底层 API
`NativeMethods.cs` 集中存放全部 P/Invoke 签名、常量、`[StructLayout]` 结构体。
- **禁止把 P-Invoke 写进 UI code-behind。** 新增原生 API 一律加到此文件并附 XML 注释。
- API 分类索引与各签名细节见 `docs/02-interaction-engine.md`「NativeMethods 索引」。

### `/Services` —— 核心逻辑（无 UI 依赖）
- `GlobalHookService` —— 全局低级鼠标钩子（`WH_MOUSE_LL`）。回调内仅同步"吞键"，UI 变化/磁盘 IO 一律 `Dispatcher.BeginInvoke` 异步；`WM_MOUSEMOVE` 旁观分支做画圈识别。详见 `docs/02-interaction-engine.md`。
- `GestureHelper` —— 纯几何纯函数画圈判定，无状态、无副作用。详见 `docs/02-interaction-engine.md`。
- `ScreenshotService` —— 多屏截图采集 + GDI 回收。详见 `docs/02-interaction-engine.md`。
- `ActionExecutor` —— 动作分发，含 `sys:` 协议路由；空命令校验 + `Process.Start` try/catch。容错细节见 `docs/02-interaction-engine.md`「崩溃兜底」。
- `SettingsManager` —— 全局单例（`Instance`），统一配置中心。详见下文「配置系统」。
- `ActionStore` —— 静态门面（`internal static class`），持动作列表与唤醒键的**内存缓存**：`Init` 启动载入（一次 IO）、`GetActions` 唤醒零 IO、`LoadForEdit` 深拷贝供编辑隔离、`UpdateCache` 保存时同步。供 `App` / `SettingsWindow` / `ActionExecutor` / `MainWindow` 使用。

### `/UI` —— WPF 视图（XAML + code-behind）
`MainWindow` / `SettingsWindow` / `ScreenshotWindow` / `PinWindow` / `BrushHelper`（JSON 颜色串 `#AARRGGBB` / 命名色转 WPF `Brush` 的静态转换器），详见 `docs/03-ui-and-styling.md`。

### `/Models` —— 数据契约
- `ActionItem` —— 实现 `INotifyPropertyChanged` 供 DataGrid 双向绑定。
- `SettingsModel` —— 多层级 POCO，默认值对齐重构前硬编码：
  - `ActionSettings`：含 `public const int WAKEUP_CIRCLE_GESTURE = -1;`（纯轨迹画圈唤醒的 `WakeupMessage` 哨兵值）+ `XButtonData`。
  - `SnippingSettings` / `MenuSettings`：各组颜色 / 尺寸 / 阈值。
  - `PinSettings`：颜色 / 尺寸 / 阴影模糊半径 / 旋转步进 / `DefaultShowBorder`（默认显示边界）/ `DefaultAnnotationMode`（默认批注模式）。

### `/Resources` —— 共享 XAML 资源
`ThemeStyles.xaml` 公共主题资源字典，由 `App.xaml` 合并，各窗口以 `StaticResource` 引用。详见 `docs/03-ui-and-styling.md`。

## 2. 配置系统（`SettingsManager` / `ActionStore`）

### `SettingsManager` —— 全局单例
统一配置中心：读写 `SettingsModel` 到 `settings.json`，源码不纳管（运行时生成/读写）。
- **加载**：`Load()` 读 `settings.json`；文件不存在时默认值 + 迁移旧 `appsettings.json`（`JsonDocument` 解析）并落盘。
- **坏文件兜底**：`ReadSettings` 捕获 `JsonException` 时把坏文件 `File.Move` 为 `settings.json.bak` 再回退默认值；其他异常回退默认值。
- **原子写**：`Save()` 先写 `settings.json.tmp` 再 `File.Move(overwrite:true)` 覆盖，防断电/崩溃截断。
- **同步 IO**（`File.ReadAllText` / `File.WriteAllText`），不引入异步以保调用时序。
- **生命周期**：`App.OnStartup` 加载一次；保存由 `SettingsWindow` 应用设置触发（`App.OnExit` 不保存）。
- **编辑隔离**：`ActionStore.LoadForEdit` 返回内存缓存的深拷贝，`SettingsWindow` 编辑该拷贝，不受他处影响；`Load()` 仅启动时调用一次。

### `ActionStore` —— 静态门面（内存缓存）
`internal static class`，持动作列表与唤醒键的内存缓存，唤醒路径零 IO（极速唤醒渲染规范 docs/03 §7.4）。供 `App` / `SettingsWindow` / `ActionExecutor` / `MainWindow` 使用。
- `Init(action)`：`App.OnStartup` 一次性载入缓存。
- `GetActions()`：返回缓存列表，供 `MainWindow` 唤醒绑定（零 IO）。
- `LoadForEdit()`：返回深拷贝，供 `SettingsWindow` 编辑隔离。
- `UpdateCache(action)`：`SettingsWindow` 保存时同步缓存（落盘由 `SettingsManager.Save` 统一完成）。

### `SettingsWindow` 5 页编辑
常规（唤醒键）/ 动作管理（DataGrid）/ 截屏 / 菜单 / 贴图，覆盖四组全部字段。"应用设置"时四组写回 `SettingsManager.Instance.Settings` + `Save()`，并调 `MainWindow.ApplyMenuSettings` 即时刷新菜单 + `RefreshActions` 重绑动作 + `ActionStore.UpdateCache` 同步缓存（`Menu`/`Action` 即时生效；`Snipping`/`Pin` 分别在下次截图/钉图时生效）。颜色字段 hex 文本框 + 实时预览；`Validate` 全字段校验，非法 `MessageBox` 拦截。排版细节见 `docs/03-ui-and-styling.md`。

## 3. 统一配置三层规范

重构后所有"硬编码"按职责分三层，**严禁再散落到 code-behind / XAML 字面量**：

### JSON（`SettingsModel` / `settings.json`）
关键视觉与交互参数（`Snipping` / `Menu` / `Pin` 的颜色、尺寸、阈值、阴影模糊半径、旋转步进等）。各窗口构造函数 `InitializeComponent()` 后读 `SettingsManager.Instance.Settings.{组}` 动态赋值给命名控件属性；按钮背景色等 Style 内部值经 `{DynamicResource}` 注入（窗口 `Resources[key] = BrushHelper.ToBrush(...)`）。

### ThemeStyles.xaml（`StaticResource`）
纯布局 / 公共样式。由 `App.xaml` 合并。**不写入 JSON**。控件清单见 `docs/03-ui-and-styling.md`。

### 保留内联
窗口独有视觉物理反馈（如 `PinWindow` 阴影 Depth/Opacity/Direction/Color、`PinBorderThickness=2`、不透明度菜单预设）与唯一面板布局约束（如 `SettingsWindow` 750×500），不提取。

> **新增可配置项决策树**：
> - 关键视觉/交互参数 → 加到 `SettingsModel` 对应组 + 默认值 + code-behind 注入；
> - 公共样式 → 加到 `ThemeStyles.xaml`；
> - 否则保留内联。

## 4. 指令协议（SDD）：`sys:` 前缀

`ActionItem.Command` 以 `sys:` 开头者为**内部协议指令**，由 `ActionExecutor.Execute` 拦截，不走 `Process.Start`。当前已实现：

| 指令 | 行为 |
|------|------|
| `sys:snipping` | 调 `ScreenshotService.Capture()` 取全屏底图，`new ScreenshotWindow(source, bounds).ShowDialog()` 打开截屏覆盖层 |

> **新增内置功能**：在 `ActionExecutor.Execute` 中加 `if (item.Command == "sys:xxx")` 分支，并在 `SettingsModel.Action` 默认动作（`SettingsManager` 首次生成 `settings.json` 时写入）或文档中登记。`sys:` 之外的命令一律按外部进程启动（`UseShellExecute=true`）。
