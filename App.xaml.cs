using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using MyQuicker.Domain.DTO;
using MyQuicker.Domain.Runtime;
using MyQuicker.Interop;
using MyQuicker.Services;
using MyQuicker.UI;
using Application = System.Windows.Application;

namespace MyQuicker;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private AppBootstrapper _bootstrapper = null!;
    private NotifyIcon? _notifyIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
#if DEBUG
        // DEBUG 日志：Debug.WriteLine 同时写文件 + 控制台。
        // · 控制台：ConsoleTraceListener 写 stdout——Debug 配置 OutputType=Exe（控制台子系统），
        //   `dotnet watch run` / `dotnet run` 把子进程 stdout 接到终端，日志直接出现。
        // · 文件：exe 同目录 debug.log（FileShare.ReadWrite 可运行时 tail），FileMode.Create 每次清空。
        // 新增调试日志一律用 Debug.WriteLine（由 ConsoleTraceListener 统一桥接到终端，勿直接 Console.WriteLine）。
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "debug.log");
            var fs = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            Trace.AutoFlush = true;
            Trace.Listeners.Add(new TextWriterTraceListener(fs));
            Trace.Listeners.Add(new ConsoleTraceListener(useErrorStream: false));
        }
        catch { /* 日志初始化失败不影响主流程 */ }
#endif

        // 全局崩溃兜底：未捕获异常记录日志并拦截，避免常驻托盘进程闪退
        // （StackOverflow/OOM 等不可恢复异常不会触发此事件）。
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;

        base.OnStartup(e);

        // 组合根：按 CLAUDE.md §2 启动链路单向构建全部运行时对象。
        _bootstrapper = new AppBootstrapper();
        _bootstrapper.Run();
        var settings = _bootstrapper.SettingsManager.Settings;

        _bootstrapper.MainWindow.OpenSettingsAction = OpenSettings;

        _bootstrapper.RawInputSource.WakeContextReceived += (s, ctx) => _bootstrapper.WakeOrchestrator.OnWakeContext(ctx);
        _bootstrapper.RawInputSource.AnyMouseDown += _bootstrapper.MainWindowOutsideClickSource.OnMouseDown;

        // 预热（docs/03 §7.2）：屏幕外 + 透明 + Show 一次，强迫 WPF 完成 XAML 解析、
        // 模板绑定与 GPU 材质编译。窗口已渲染在内存，用户不可见；唤醒仅瞬移+显透明度。
        _bootstrapper.MainWindow.WindowStartupLocation = WindowStartupLocation.Manual;
        _bootstrapper.MainWindow.Left = -9999;
        _bootstrapper.MainWindow.Top = -9999;
        _bootstrapper.MainWindow.Opacity = 0;
        _bootstrapper.MainWindow.Show();

        InitializeTray();

        // 启动 toast：主窗口无任务栏入口，需明确告知已启动 + 提示当前唤醒方式。
        string hint = GetWakeupHint(settings.TriggerBindings.FirstOrDefault());
        Toast.Show($"aurora 已启动 · {hint}唤醒");

        // 低级鼠标钩子挂载时机（防全局鼠标卡死）：
        // WH_MOUSE_LL 回调在安装线程的消息循环里 dispatch。若钩子在托盘注册
        // （Shell_NotifyIcon 与 explorer 同步通信）与 Toast 渲染之前挂载，这些
        // 主线程同步耗时会让回调排队超时（LowLevelHooksTimeout，默认 300ms），
        // 触发全局鼠标卡死。故物理后移到启动收尾之后，并用 Dispatcher 闲置优先级
        // 派发，强制推迟到下一帧消息循环 Pump 完所有积压 UI 消息、主线程回归静默
        // 后再激活钩子。
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            _bootstrapper.RawInputSource.Start();
        }), DispatcherPriority.Background);

#if !DEBUG
        // 后台检查更新（非阻塞）：发现新版弹框，用户同意后下载 MSI 静默升级。
        _ = CheckForUpdatesAsync();
#endif
    }

    /// <summary>后台检查 GitHub Releases 更新；发现新版提示用户升级。</summary>
    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var checker = new UpdateChecker();
            var info = await checker.CheckAsync().ConfigureAwait(false);
            if (info is null) return;

            await Dispatcher.InvokeAsync(() =>
            {
                if (System.Windows.MessageBox.Show(
                        $"发现新版本 {info.Version}，是否立即更新？",
                        "MyQuicker 更新", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    _ = checker.ApplyAsync(info);
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Update] check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets up the system tray icon with its context menu: 设置... / 退出.
    /// </summary>
    private static string GetWakeupHint(TriggerBinding? binding)
    {
        if (binding is null)
            return "中键";

        return binding.Type switch
        {
            TriggerType.CircleGesture => "画圈",
            TriggerType.Button when binding.WakeupMessage == NativeMethods.WM_XBUTTONDOWN => "侧键",
            _ => "中键"
        };
    }

    private void InitializeTray()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "aurora",
            Visible = true,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("设置...").Click += (_, _) => OpenSettings();
        menu.Items.Add("退出").Click += (_, _) => Application.Current.Shutdown();
        _notifyIcon.ContextMenuStrip = menu;
    }

    private void OpenSettings()
    {
        if (_bootstrapper is null)
            return;

        // 单例：已存在则激活前置，不重复创建（防画圈唤醒菜单后再次点齿轮开出第二个设置页）。
        var existing = Application.Current?.Windows.OfType<SettingsWindow>().FirstOrDefault();
        if (existing is not null)
        {
            existing.Activate();
            existing.Topmost = true;  // 强行置顶，穿透系统前台锁拦截
            existing.Topmost = false;
            existing.Focus();
            return;
        }

        var window = new SettingsWindow(_bootstrapper.SettingsManager, new SettingsBuilder());
        window.SettingsSaved += (_, _) => _bootstrapper.RebuildRuntime();
        window.Show();
        window.Activate();
        window.Topmost = true;  // 强行置顶，穿透系统前台锁拦截
        window.Topmost = false; // 瞬间取消，恢复正常层级以免遮挡其他窗口
        window.Focus();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_bootstrapper?.RawInputSource is { } source)
        {
            source.AnyMouseDown -= _bootstrapper.MainWindowOutsideClickSource.OnMouseDown;
            source.Stop();
            source.Dispose();
        }

        _notifyIcon?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// 全局未捕获异常兜底：分类拦截并完整记录，严禁无条件吞掉所有异常。
    /// 安全与权限类异常被视为致命错误，强制终止进程以避免系统处于未定义状态。
    /// </summary>
    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Exception ex = e.Exception;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[FATAL] Unhandled dispatcher exception: {ex.GetType().FullName}");
        sb.AppendLine($"Message: {ex.Message}");
        sb.AppendLine($"StackTrace:\n{ex.StackTrace}");
        if (ex.InnerException is not null)
        {
            sb.AppendLine($"Inner exception: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
            sb.AppendLine($"Inner stack trace:\n{ex.InnerException.StackTrace}");
        }
        string logMessage = sb.ToString();

        Debug.WriteLine(logMessage);
        Trace.WriteLine(logMessage);

        // 安全/权限类异常是毁灭性的：继续运行可能让进程处于不可信的僵尸状态。
        if (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            try
            {
                _notifyIcon?.Dispose();
                _bootstrapper?.RawInputSource.Stop();
                _bootstrapper?.RawInputSource.Dispose();
            }
            catch
            {
                // 清理失败不影响强制终止。
            }

            Environment.FailFast("Security/permission exception forced process termination.", ex);
        }

        // 其它未处理异常不再无条件吞掉：交给运行时处理，避免隐藏数据损坏或逻辑错误。
        e.Handled = false;
    }
}
