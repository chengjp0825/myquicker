using System.Drawing;
using System.Windows;
using System.Windows.Forms;
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
    private GlobalHookService? _hookService;
    private MainWindow? _mainWindow;
    private NotifyIcon? _notifyIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 挂接到父进程控制台（如 dotnet run 的终端），使 WinExe 的
        // Console.WriteLine 调试输出直接显示在那里。无父控制台时静默失败。
        NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);

        // 全局崩溃兜底：未捕获异常记录日志并拦截，避免常驻托盘进程闪退
        // （StackOverflow/OOM 等不可恢复异常不会触发此事件）。
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;

        base.OnStartup(e);

        // 统一配置：加载 settings.json（首次自动迁移旧 appsettings.json 的唤醒键与动作列表）。
        SettingsManager.Instance.Load();

        _hookService = new GlobalHookService();
        _hookService.UpdateSettings(SettingsManager.Instance.Settings.Action);

        _mainWindow = new MainWindow();
        _mainWindow.OpenSettingsAction = OpenSettings;
        _hookService.OnWakeupClick += _mainWindow.OnHookWakeupClick;
        _hookService.OnAnyMouseDown += _mainWindow.OnAnyMouseDown;
        _hookService.Start();

        InitializeTray();
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

        var window = new SettingsWindow(_hookService, new ActionStore());
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
        Console.WriteLine($"ERROR: 未捕获异常已拦截: {e.Exception}");
        Console.Out.Flush();
        e.Handled = true;
    }
}
