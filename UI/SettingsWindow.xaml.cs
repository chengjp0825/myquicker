using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using MyQuicker.Domain.DTO;
using MyQuicker.Domain.Runtime;
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
    private readonly TriggerBinding _triggerBinding;
    private readonly SnippingSettings _snipping;
    private readonly MenuSettings _menu;
    private readonly PinSettings _pin;
    private readonly List<MenuGroup> _menuGroups;

    /// <summary>设置保存并落盘后触发；订阅方（组合根）负责重建运行时对象。</summary>
    internal event EventHandler? SettingsSaved;

    internal SettingsWindow(SettingsManager settingsManager)
    {
        InitializeComponent();
        _settingsManager = settingsManager ?? throw new System.ArgumentNullException(nameof(settingsManager));

        // ActionStore.LoadForEdit() 返回内存缓存的深拷贝（隔离未保存编辑，无 IO）。
        _menuGroups = ActionStore.LoadForEdit();

        var s = settingsManager.Settings;
        // 对设置子对象做深拷贝：编辑期间仅修改副本，点“取消/X”不会污染内存中的 live DTO。
        _triggerBinding = s.TriggerBindings.FirstOrDefault() is { } tb ? Clone(tb) : new TriggerBinding();
        _snipping = Clone(s.Preferences.Snipping);
        _menu = Clone(s.Preferences.Menu);
        _pin = Clone(s.Preferences.Pin);

        PopulateControls();
        WireColorPreviews();
    }

    private void PopulateControls()
    {
        WakeupKeyCombo.SelectedIndex = ToIndex(_triggerBinding);
        InterceptWakeupCheckBox.IsChecked = _triggerBinding.InterceptWakeupKey;
        CircleSensitivityCombo.SelectedIndex = (int)_triggerBinding.CircleSensitivity;

        // 动作网格绑定到默认分组的 Action 列表；当前 UI 仅支持单分组编辑。
        var defaultGroup = _menuGroups.FirstOrDefault() ?? new MenuGroup { Id = "default", DisplayName = "默认", Icon = "EFA8" };
        ActionsGrid.ItemsSource = defaultGroup.Actions;

        // Snipping
        SnippingDragThresholdBox.Text = _snipping.DragThreshold.ToString(CultureInfo.InvariantCulture);
        SnippingMaskAlphaBox.Text = _snipping.MaskAlpha.ToString(CultureInfo.InvariantCulture);
        SnippingBorderColorBox.Text = _snipping.BorderColor;
        AfterScreenshotCombo.SelectedIndex = (int)_snipping.AfterScreenshot;
        CaptureScopeCombo.SelectedIndex = (int)_snipping.CaptureScope;

        // Menu
        MenuWidthBox.Text = _menu.Width.ToString(CultureInfo.InvariantCulture);
        MenuHeightBox.Text = _menu.Height.ToString(CultureInfo.InvariantCulture);
        MenuBackgroundBox.Text = _menu.Background;
        MenuCornerRadiusBox.Text = _menu.CornerRadius.ToString(CultureInfo.InvariantCulture);
        MenuButtonBgBox.Text = _menu.ButtonBackground;
        MenuButtonHoverBgBox.Text = _menu.ButtonHoverBackground;

        // Pin
        PinBorderColorBox.Text = _pin.BorderColor;
        PinDefaultOpacityBox.Text = _pin.DefaultOpacity.ToString(CultureInfo.InvariantCulture);
        PinDefaultShowBorderBox.IsChecked = _pin.DefaultShowBorder;
        PinDefaultAnnotationModeBox.IsChecked = _pin.DefaultAnnotationMode;
        PinDefaultTopmostBox.IsChecked = _pin.DefaultTopmost;
        PinDefaultShowShadowBox.IsChecked = _pin.DefaultShowShadow;
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

        // Trigger binding
        int index = WakeupKeyCombo.SelectedIndex;
        _triggerBinding.Type = index == 2 ? TriggerType.CircleGesture : TriggerType.Button;
        _triggerBinding.WakeupMessage = index switch
        {
            1 => NativeMethods.WM_XBUTTONDOWN,
            2 => null,
            _ => NativeMethods.WM_MBUTTONDOWN,
        };
        _triggerBinding.XButtonData = index == 1 ? 1 : null;
        _triggerBinding.InterceptWakeupKey = InterceptWakeupCheckBox.IsChecked == true;
        _triggerBinding.CircleSensitivity = (CircleSensitivity)CircleSensitivityCombo.SelectedIndex;

        // Snipping
        _snipping.DragThreshold = double.Parse(SnippingDragThresholdBox.Text, CultureInfo.InvariantCulture);
        _snipping.MaskAlpha = double.Parse(SnippingMaskAlphaBox.Text, CultureInfo.InvariantCulture);
        _snipping.BorderColor = SnippingBorderColorBox.Text;
        _snipping.AfterScreenshot = (SnippingAfterScreenshot)AfterScreenshotCombo.SelectedIndex;
        _snipping.CaptureScope = (SnippingCaptureScope)CaptureScopeCombo.SelectedIndex;

        // Menu
        _menu.Width = double.Parse(MenuWidthBox.Text, CultureInfo.InvariantCulture);
        _menu.Height = double.Parse(MenuHeightBox.Text, CultureInfo.InvariantCulture);
        _menu.Background = MenuBackgroundBox.Text;
        _menu.CornerRadius = int.Parse(MenuCornerRadiusBox.Text, CultureInfo.InvariantCulture);
        _menu.ButtonBackground = MenuButtonBgBox.Text;
        _menu.ButtonHoverBackground = MenuButtonHoverBgBox.Text;

        // Pin
        _pin.BorderColor = PinBorderColorBox.Text;
        _pin.DefaultOpacity = double.Parse(PinDefaultOpacityBox.Text, CultureInfo.InvariantCulture);
        _pin.DefaultShowBorder = PinDefaultShowBorderBox.IsChecked == true;
        _pin.DefaultAnnotationMode = PinDefaultAnnotationModeBox.IsChecked == true;
        _pin.DefaultTopmost = PinDefaultTopmostBox.IsChecked == true;
        _pin.DefaultShowShadow = PinDefaultShowShadowBox.IsChecked == true;

        // 确保默认分组存在。
        var defaultGroup = _menuGroups.FirstOrDefault();
        if (defaultGroup is null)
        {
            defaultGroup = new MenuGroup { Id = "default", DisplayName = "默认", Icon = "EFA8" };
            _menuGroups.Add(defaultGroup);
        }

        // 构建全新的 Settings DTO，禁止就地修补原有 live DTO。
        var newSettings = new Settings
        {
            TriggerBindings = new List<TriggerBinding> { _triggerBinding },
            Preferences = new Preferences
            {
                Snipping = _snipping,
                Menu = _menu,
                Pin = _pin,
            },
            MenuGroups = _menuGroups,
        };

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

    /// <summary>深拷贝 DTO 子对象；编辑期与 live DTO 隔离。</summary>
    private static T Clone<T>(T source) where T : class, new()
    {
        string json = JsonSerializer.Serialize(source, SettingsManager.JsonOptions);
        return JsonSerializer.Deserialize<T>(json, SettingsManager.JsonOptions) ?? new T();
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
