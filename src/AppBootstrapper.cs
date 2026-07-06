using System;
using System.Linq;
using MyQuicker.Domain.DTO;
using MyQuicker.Domain.Runtime;
using MyQuicker.Domain.Runtime.Commands;
using MyQuicker.Services;
using MyQuicker.UI;

namespace MyQuicker;

/// <summary>
/// 应用程序最高拓扑装配中心（Composition Root）。
/// 负责按 CLAUDE.md §2 的启动链路单向构建运行时对象：
/// Settings → CommandRegistry → ActionExecutor → MainWindow → WakeOrchestrator → RawInputSource。
/// 设置保存后通过 <see cref="RebuildRuntime"/> 重新走完整工厂链路，禁止就地修补运行时对象。
/// </summary>
internal sealed class AppBootstrapper
{
    private readonly SettingsManager _settingsManager;
    private readonly ITimeProvider _timeProvider;
    private readonly IProcessLauncher _processLauncher;
    private readonly IScreenGeometry _screenGeometry;
    private readonly ISynchronizationContext _syncContext;

    private RawInputSource _rawInputSource = null!;
    private TriggerEvaluator _triggerEvaluator = null!;
    private WakeOrchestrator _wakeOrchestrator = null!;
    private MainWindow _mainWindow = null!;
    private MainWindowOutsideClickSource _mainWindowOutsideClickSource = null!;
    private CommandContext _commandContext = null!;
    private ActionExecutor _actionExecutor = null!;

    public SettingsManager SettingsManager => _settingsManager;
    public MainWindow MainWindow => _mainWindow;
    public MainWindowOutsideClickSource MainWindowOutsideClickSource => _mainWindowOutsideClickSource;
    public RawInputSource RawInputSource => _rawInputSource;
    public TriggerEvaluator TriggerEvaluator => _triggerEvaluator;
    public WakeOrchestrator WakeOrchestrator => _wakeOrchestrator;

    public AppBootstrapper()
    {
        _settingsManager = new SettingsManager();
        _timeProvider = new SystemTimeProvider();
        _processLauncher = new ProcessLauncher();
        _screenGeometry = new FormsScreenGeometry();
        _syncContext = new WpfSynchronizationContext();
    }

    /// <summary>运行启动链路并创建所有运行时对象。</summary>
    public void Run()
    {
        Settings settings = _settingsManager.Load();
        BuildRuntime(settings);
    }

    /// <summary>设置保存后重新走完整工厂链路，全量重建运行时对象。</summary>
    public void RebuildRuntime()
    {
        Settings settings = _settingsManager.Settings;
        BuildRuntime(settings);
    }

    private void BuildRuntime(Settings settings)
    {
        // 1. 运行时基础设施：截图服务与截图工作流按最新设置重建。
        var screenshotCaptureService = new ScreenshotCaptureService(settings.Preferences.Snipping);
        var screenshotOverlay = new ScreenshotOverlayAdapter(settings.Preferences.Snipping);
        var screenshotPinService = new ScreenshotPinServiceAdapter(settings.Preferences.Pin);
        var toastService = new ToastService();
        var screenshotWorkflow = new ScreenshotWorkflow(
            screenshotCaptureService,
            screenshotOverlay,
            screenshotPinService,
            toastService,
            settings.Preferences.Snipping,
            settings.Preferences.Pin);
        _commandContext = new CommandContext(_processLauncher, screenshotCaptureService, screenshotWorkflow, toastService);

        // 2. 构建命令注册中心：先内建、后用户，保证内建命令不被用户配置覆盖。
        var registry = new CommandRegistry();
        BuiltInCommandProvider.Register(registry);
        UserCommandStore.Register(registry, settings.Commands);

        // 3. 构建动作执行调度中心。
        _actionExecutor = new ActionExecutor(registry, settings.Commands);

        // 4. 主窗口：首次创建，后续重新绑定依赖；MenuGroups 由构造注入。
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow(_actionExecutor, _commandContext, settings.Preferences, settings.MenuGroups);
        }
        else
        {
            _mainWindow.RebindRuntime(_actionExecutor, _commandContext, settings.Preferences, settings.MenuGroups);
        }

        // 5. 唤醒策略中枢：首次创建，后续更新设置。
        //    阻塞策略与外部点击源与主窗口生命周期绑定，首次创建后复用。
        _mainWindowOutsideClickSource ??= new MainWindowOutsideClickSource(_mainWindow);
        var wakeBlockPolicy = new OverlayWakeBlockPolicy();

        var orchestratorSettings = new WakeOrchestratorSettings(
            DebounceInterval: TimeSpan.FromMilliseconds(200),
            StaleEventThreshold: TimeSpan.FromSeconds(1),
            MenuWidth: settings.Preferences.Menu.Width,
            MenuHeight: settings.Preferences.Menu.Height);

        if (_wakeOrchestrator is null)
        {
            _wakeOrchestrator = new WakeOrchestrator(
                _mainWindow,
                _screenGeometry,
                _timeProvider,
                wakeBlockPolicy,
                _mainWindowOutsideClickSource,
                orchestratorSettings);
        }
        else
        {
            _wakeOrchestrator.UpdateSettings(orchestratorSettings);
        }

        // 6. 触发器评估器：首次创建，后续只更新触发器列表。
        _triggerEvaluator ??= new TriggerEvaluator();

        var triggers = settings.TriggerBindings
            .Select(b => TriggerFactory.Create(b, _timeProvider))
            .ToList();
        _triggerEvaluator.UpdateTriggers(triggers);

        // 7. 原始输入源：首次创建，后续只更新拦截策略与触发器；钩子句柄保持复用。
        bool interceptWakeupKey = settings.TriggerBindings.All(b => b.InterceptWakeupKey);
        var interceptionPolicy = new InputInterceptionPolicy(interceptWakeupKey);

        if (_rawInputSource is null)
        {
            _rawInputSource = new RawInputSource(_syncContext, _timeProvider, _triggerEvaluator, interceptionPolicy);
        }
        else
        {
            _rawInputSource.UpdateInterceptionPolicy(interceptionPolicy);
        }
    }
}
