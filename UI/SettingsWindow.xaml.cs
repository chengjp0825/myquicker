using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using MyQuicker.Domain.DTO;
using MyQuicker.Interop;
using MyQuicker.Services;

namespace MyQuicker.UI;

/// <summary>
/// 设置中心：常规(唤醒键) / 动作管理 / 截屏与贴图 / 菜单 四页。
/// 保存时构建全新的 <see cref="Settings"/> DTO 并通过 <see cref="SettingsManager"/> 落盘，
/// 随后触发 <see cref="SettingsSaved"/> 事件通知组合根执行全量重建。
/// SettingsWindow 本身禁止就地修补运行时对象或调用主窗口方法。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsManager _settingsManager;
    private readonly SettingsBuilder _settingsBuilder;
    private readonly SettingsViewModel _viewModel;

    /// <summary>设置保存并落盘后触发；订阅方（组合根）负责重建运行时对象。</summary>
    internal event EventHandler? SettingsSaved;

    internal SettingsWindow(SettingsManager settingsManager, SettingsBuilder? builder = null)
    {
        InitializeComponent();
        _settingsManager = settingsManager ?? throw new System.ArgumentNullException(nameof(settingsManager));
        _settingsBuilder = builder ?? new SettingsBuilder();
        _viewModel = new SettingsViewModel();
        _viewModel.LoadFrom(settingsManager.Settings);

        DataContext = _viewModel;
        PopulateControls();
        WireColorPreviews();
    }

    private void PopulateControls()
    {
        WakeupKeyCombo.SelectedIndex = ToIndex(_viewModel.TriggerBinding);
        InterceptWakeupCheckBox.IsChecked = _viewModel.TriggerBinding.InterceptWakeupKey;
        CircleSensitivityCombo.SelectedIndex = (int)_viewModel.TriggerBinding.CircleSensitivity;

        // 动作网格绑定到默认分组的 Action 列表；当前 UI 仅支持单分组编辑。
        // 首次运行/无分组时把默认分组写回 ViewModel，避免保存时创建另一个分组导致编辑丢失。
        var defaultGroup = _viewModel.MenuGroups.FirstOrDefault();
        if (defaultGroup is null)
        {
            defaultGroup = new MenuGroup { Id = "default", DisplayName = "默认", Icon = "EFA8" };
            _viewModel.MenuGroups.Add(defaultGroup);
        }
        ActionsGrid.ItemsSource = defaultGroup.Actions;

        // Snipping
        SnippingDragThresholdBox.Text = _viewModel.Snipping.DragThreshold.ToString(CultureInfo.InvariantCulture);
        SnippingMaskAlphaBox.Text = _viewModel.Snipping.MaskAlpha.ToString(CultureInfo.InvariantCulture);
        SnippingBorderColorBox.Text = _viewModel.Snipping.BorderColor;
        AfterScreenshotCombo.SelectedIndex = (int)_viewModel.Snipping.AfterScreenshot;
        CaptureScopeCombo.SelectedIndex = (int)_viewModel.Snipping.CaptureScope;

        // Menu
        MenuWidthBox.Text = _viewModel.Menu.Width.ToString(CultureInfo.InvariantCulture);
        MenuHeightBox.Text = _viewModel.Menu.Height.ToString(CultureInfo.InvariantCulture);
        MenuBackgroundBox.Text = _viewModel.Menu.Background;
        MenuCornerRadiusBox.Text = _viewModel.Menu.CornerRadius.ToString(CultureInfo.InvariantCulture);
        MenuButtonBgBox.Text = _viewModel.Menu.ButtonBackground;
        MenuButtonHoverBgBox.Text = _viewModel.Menu.ButtonHoverBackground;

        // Pin
        PinBorderColorBox.Text = _viewModel.Pin.BorderColor;
        PinDefaultOpacityBox.Text = _viewModel.Pin.DefaultOpacity.ToString(CultureInfo.InvariantCulture);
        PinDefaultShowBorderBox.IsChecked = _viewModel.Pin.DefaultShowBorder;
        PinDefaultAnnotationModeBox.IsChecked = _viewModel.Pin.DefaultAnnotationMode;
        PinDefaultTopmostBox.IsChecked = _viewModel.Pin.DefaultTopmost;
        PinDefaultShowShadowBox.IsChecked = _viewModel.Pin.DefaultShowShadow;
    }

    private void WireColorPreviews()
    {
        WireColorPreview(SnippingBorderColorBox, SnippingBorderColorPreview);
        WireColorPreview(MenuBackgroundBox, MenuBackgroundPreview);
        WireColorPreview(MenuButtonBgBox, MenuButtonBgPreview);
        WireColorPreview(MenuButtonHoverBgBox, MenuButtonHoverBgPreview);
        WireColorPreview(PinBorderColorBox, PinBorderColorPreview);
    }

    /// <summary>把 TextBox 的颜色串实时映射到预览色块；点色块弹 ColorDialog 选色。</summary>
    private static void WireColorPreview(System.Windows.Controls.TextBox box, System.Windows.Controls.Border preview)
    {
        UpdateColorPreview(box, preview);
        box.TextChanged += (s, e) => UpdateColorPreview(box, preview);
        preview.MouseLeftButtonDown += (s, e) => OpenColorPicker(box);
    }

    private static void UpdateColorPreview(System.Windows.Controls.TextBox box, System.Windows.Controls.Border preview)
    {
        try { preview.Background = BrushHelper.ToBrush(box.Text); }
        catch { preview.Background = System.Windows.Media.Brushes.Transparent; } // 透明保命中（点色块选色）
    }

    /// <summary>点色块弹 WinForms ColorDialog 选色；保留当前 alpha（对话框不支持选 alpha），返回 #AARRGGBB 写回 TextBox。</summary>
    private static void OpenColorPicker(System.Windows.Controls.TextBox box)
    {
        byte alpha = 255;
        System.Windows.Media.Color current = System.Windows.Media.Colors.Black;
        try
        {
            if (BrushHelper.ToBrush(box.Text) is System.Windows.Media.SolidColorBrush sc)
            {
                current = sc.Color;
                alpha = current.A;
            }
        }
        catch { }

        var dlgColor = System.Drawing.Color.FromArgb(alpha, current.R, current.G, current.B);
        using var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = dlgColor };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var c = dlg.Color;
            box.Text = $"#{alpha:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        }
    }

    private static int ToIndex(TriggerBinding binding)
    {
        if (binding.Type == TriggerType.CircleGesture)
            return 2; // 画圈
        if (binding.WakeupMessage == NativeMethods.WM_XBUTTONDOWN)
            return 1; // 侧键后退 (XButton1)
        return 0; // 中键
    }

    /// <summary>
    /// 把当前 UI 下拉索引转换为 SettingsBuilder 期望的唤醒键索引。
    /// UI 下拉仅有 3 项（0=中键 / 1=侧键 / 2=画圈），Builder 用 3 表示画圈。
    /// </summary>
    private static int ToBuilderWakeupKeyIndex(int uiIndex) => uiIndex == 2 ? 3 : uiIndex;

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _ = SaveAndCloseAsync();
    }

    /// <summary>
    /// 异步保存设置：验证与 DTO 构造在 UI 线程完成，文件 I/O 离屏执行，
    /// 保存成功后通知组合根重建并关闭窗口。
    /// </summary>
    private async Task SaveAndCloseAsync()
    {
        // Commit any in-flight cell edit before persisting.
        ActionsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);

        if (!Validate(out string error))
        {
            System.Windows.MessageBox.Show(error, "设置", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        // Sync trigger binding from UI into the view-model copy.
        var triggerBinding = _settingsBuilder.BuildTriggerBinding(
            ToBuilderWakeupKeyIndex(WakeupKeyCombo.SelectedIndex),
            InterceptWakeupCheckBox.IsChecked == true,
            CircleSensitivityCombo.SelectedIndex);
        _viewModel.TriggerBinding.Type = triggerBinding.Type;
        _viewModel.TriggerBinding.WakeupMessage = triggerBinding.WakeupMessage;
        _viewModel.TriggerBinding.XButtonData = triggerBinding.XButtonData;
        _viewModel.TriggerBinding.InterceptWakeupKey = triggerBinding.InterceptWakeupKey;
        _viewModel.TriggerBinding.CircleSensitivity = triggerBinding.CircleSensitivity;

        // Snipping
        _viewModel.Snipping.DragThreshold = double.Parse(SnippingDragThresholdBox.Text, CultureInfo.InvariantCulture);
        _viewModel.Snipping.MaskAlpha = double.Parse(SnippingMaskAlphaBox.Text, CultureInfo.InvariantCulture);
        _viewModel.Snipping.BorderColor = SnippingBorderColorBox.Text;
        _viewModel.Snipping.AfterScreenshot = (SnippingAfterScreenshot)AfterScreenshotCombo.SelectedIndex;
        _viewModel.Snipping.CaptureScope = (SnippingCaptureScope)CaptureScopeCombo.SelectedIndex;

        // Menu
        _viewModel.Menu.Width = double.Parse(MenuWidthBox.Text, CultureInfo.InvariantCulture);
        _viewModel.Menu.Height = double.Parse(MenuHeightBox.Text, CultureInfo.InvariantCulture);
        _viewModel.Menu.Background = MenuBackgroundBox.Text;
        _viewModel.Menu.CornerRadius = int.Parse(MenuCornerRadiusBox.Text, CultureInfo.InvariantCulture);
        _viewModel.Menu.ButtonBackground = MenuButtonBgBox.Text;
        _viewModel.Menu.ButtonHoverBackground = MenuButtonHoverBgBox.Text;

        // Pin
        _viewModel.Pin.BorderColor = PinBorderColorBox.Text;
        _viewModel.Pin.DefaultOpacity = double.Parse(PinDefaultOpacityBox.Text, CultureInfo.InvariantCulture);
        _viewModel.Pin.DefaultShowBorder = PinDefaultShowBorderBox.IsChecked == true;
        _viewModel.Pin.DefaultAnnotationMode = PinDefaultAnnotationModeBox.IsChecked == true;
        _viewModel.Pin.DefaultTopmost = PinDefaultTopmostBox.IsChecked == true;
        _viewModel.Pin.DefaultShowShadow = PinDefaultShowShadowBox.IsChecked == true;

        // 确保默认分组存在。
        var defaultGroup = _viewModel.MenuGroups.FirstOrDefault();
        if (defaultGroup is null)
        {
            defaultGroup = new MenuGroup { Id = "default", DisplayName = "默认", Icon = "EFA8" };
            _viewModel.MenuGroups.Add(defaultGroup);
        }

        // 保存前把用户在网格中输入/编辑的 Command 字符串迁移到 Commands 目录，
        // 确保每个 ActionItem 都有稳定的 CommandId，同时保留用户在网格中可见的路径/URL。
        var migrationSettings = new Settings
        {
            MenuGroups = _viewModel.MenuGroups,
            Commands = _viewModel.Commands,
        };
        SettingsManager.MigrateActionCommandsIntoCatalog(migrationSettings);

        // 通过 ViewModel + Builder 构建全新的 Settings DTO，禁止就地修补原有 live DTO。
        var newSettings = _viewModel.Build(_settingsBuilder);

        try
        {
            await _settingsManager.SaveAsync(newSettings).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ERROR: 保存设置失败: {ex.Message}");
            await Dispatcher.InvokeAsync(() =>
                System.Windows.MessageBox.Show($"保存设置失败：{ex.Message}", "设置",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error));
            return;
        }

        // 通知组合根执行全量重建：触发器、命令注册表、动作缓存、菜单外观等。
        await Dispatcher.InvokeAsync(() =>
        {
            SettingsSaved?.Invoke(this, EventArgs.Empty);
            Close();
        });
    }

    /// <summary>校验全部数值与颜色字段；返回 false 时 error 给出提示。</summary>
    private bool Validate(out string error)
    {
        if (!double.TryParse(SnippingDragThresholdBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        { error = "“点击/拖拽阈值”需为数字。"; return false; }
        if (!double.TryParse(SnippingMaskAlphaBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double ma) || ma < 0 || ma > 1)
        { error = "“暗罩浓度”需为 0~1 的数字。"; return false; }
        if (!double.TryParse(MenuWidthBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        { error = "“面板宽度”需为数字。"; return false; }
        if (!double.TryParse(MenuHeightBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        { error = "“面板高度”需为数字。"; return false; }
        if (!int.TryParse(MenuCornerRadiusBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        { error = "“圆角半径”需为整数。"; return false; }
        if (!double.TryParse(PinDefaultOpacityBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double op) || op < 0 || op > 1)
        { error = "“默认不透明度”需为 0~1 的数字。"; return false; }

        if (!IsValidColor(SnippingBorderColorBox) ||
            !IsValidColor(MenuBackgroundBox) || !IsValidColor(MenuButtonBgBox) ||
            !IsValidColor(MenuButtonHoverBgBox) || !IsValidColor(PinBorderColorBox))
        { error = "颜色字段需为 #AARRGGBB 或命名色（如 Black）。"; return false; }

        error = string.Empty;
        return true;
    }

    private static bool IsValidColor(System.Windows.Controls.TextBox box)
    {
        try { BrushHelper.ToBrush(box.Text); return true; }
        catch { return false; }
    }

    /// <summary>窗口关闭时取消 SettingsSaved 订阅，避免委托持有窗口引用。</summary>
    protected override void OnClosed(EventArgs e)
    {
        SettingsSaved = null;
        base.OnClosed(e);
    }
}
