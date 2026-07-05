using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using MyQuicker.Interop;
using MyQuicker.Models;
using MyQuicker.Services;
using MyQuicker.UI;
using Application = System.Windows.Application;

namespace MyQuicker;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private GlobalHookService? _hookService;
    private MainWindow? _mainWindow;
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

        // 统一配置：加载 settings.json（首次自动迁移旧 appsettings.json 的唤醒键与动作列表）。
        SettingsManager.Instance.Load();
        // 动作数据一次性载入内存缓存，唤醒路径零 IO（docs/03 §7.4）。
        ActionStore.Init(SettingsManager.Instance.Settings.Action);

        _hookService = new GlobalHookService();
        _hookService.UpdateSettings(SettingsManager.Instance.Settings.Action);

        _mainWindow = new MainWindow();
        _mainWindow.OpenSettingsAction = OpenSettings;
        _hookService.OnWakeupClick += _mainWindow.OnHookWakeupClick;
        _hookService.OnAnyMouseDown += _mainWindow.OnAnyMouseDown;

        // 预热（docs/03 §7.2）：屏幕外 + 透明 + Show 一次，强迫 WPF 完成 XAML 解析、
        // 模板绑定与 GPU 材质编译。窗口已渲染在内存，用户不可见；唤醒仅瞬移+显透明度。
        _mainWindow.WindowStartupLocation = WindowStartupLocation.Manual;
        _mainWindow.Left = -9999;
        _mainWindow.Top = -9999;
        _mainWindow.Opacity = 0;
        _mainWindow.Show();

        _hookService.Start();

        InitializeTray();

        // 启动 toast：主窗口无任务栏入口，需明确告知已启动 + 提示当前唤醒方式。
        var action = SettingsManager.Instance.Settings.Action;
        string hint = action.WakeupMessage == ActionSettings.WAKEUP_CIRCLE_GESTURE ? "画圈"
                    : action.WakeupMessage == NativeMethods.WM_XBUTTONDOWN ? "侧键"
                    : "中键";
        Toast.Show($"MyQuicker 已启动 · {hint}唤醒");
    }

    /// <summary>
    /// Sets up the system tray icon with its context menu: 设置... / 退出.
    /// </summary>
    private void InitializeTray()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "MyQuicker",
            Visible = true,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("设置...").Click += (_, _) => OpenSettings();
        menu.Items.Add("退出").Click += (_, _) => Application.Current.Shutdown();
        _notifyIcon.ContextMenuStrip = menu;
    }

    private void OpenSettings()
    {
        if (_hookService is null)
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

        var window = new SettingsWindow(_hookService, _mainWindow);
        window.Show();
        window.Activate();
        window.Topmost = true;  // 强行置顶，穿透系统前台锁拦截
        window.Topmost = false; // 瞬间取消，恢复正常层级以免遮挡其他窗口
        window.Focus();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_hookService is not null)
        {
            if (_mainWindow is not null)
            {
                _hookService.OnWakeupClick -= _mainWindow.OnHookWakeupClick;
                _hookService.OnAnyMouseDown -= _mainWindow.OnAnyMouseDown;
            }

            _hookService.Stop();
            _hookService.Dispose();
        }

        _notifyIcon?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// 全局未捕获异常兜底：记录后拦截，保活常驻托盘进程。
    /// </summary>
    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Debug.WriteLine($"ERROR: 未捕获异常已拦截: {e.Exception}");
        e.Handled = true;
    }
}
