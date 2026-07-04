using System.Globalization;
using System.Windows;
using MyQuicker.Interop;
using MyQuicker.Models;
using MyQuicker.Services;

namespace MyQuicker.UI;

/// <summary>
/// 设置中心：常规(唤醒键) / 动作管理 / 截屏 / 菜单 / 贴图 五页。
/// 应用时把全部四组写回 SettingsManager 落盘，并即时把 Menu 组刷到 MainWindow。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly GlobalHookService _hookService;
    private readonly MainWindow? _mainWindow;
    private readonly ActionSettings _action;
    private readonly SnippingSettings _snipping;
    private readonly MenuSettings _menu;
    private readonly PinSettings _pin;

    internal SettingsWindow(GlobalHookService hookService, MainWindow? mainWindow)
    {
        InitializeComponent();
        _hookService = hookService;
        _mainWindow = mainWindow;

        // ActionStore.Load() 重读磁盘，随后抓同一份 Settings 的其余三组引用。
        _action = ActionStore.Load();
        var s = SettingsManager.Instance.Settings;
        _snipping = s.Snipping;
        _menu = s.Menu;
        _pin = s.Pin;

        PopulateControls();
        WireColorPreviews();
    }

    private void PopulateControls()
    {
        WakeupKeyCombo.SelectedIndex = ToIndex(_action);
        ActionsGrid.ItemsSource = _action.Actions;

        // Snipping
        SnippingDragThresholdBox.Text = _snipping.DragThreshold.ToString(CultureInfo.InvariantCulture);
        SnippingMaskColorBox.Text = _snipping.MaskColor;
        SnippingBorderColorBox.Text = _snipping.BorderColor;
        SnippingBorderThicknessBox.Text = _snipping.BorderThickness.ToString(CultureInfo.InvariantCulture);
        SnippingOverlayBgBox.Text = _snipping.OverlayBackground;

        // Menu
        MenuWidthBox.Text = _menu.Width.ToString(CultureInfo.InvariantCulture);
        MenuHeightBox.Text = _menu.Height.ToString(CultureInfo.InvariantCulture);
        MenuBackgroundBox.Text = _menu.Background;
        MenuCornerRadiusBox.Text = _menu.CornerRadius.ToString(CultureInfo.InvariantCulture);
        MenuButtonBgBox.Text = _menu.ButtonBackground;
        MenuButtonHoverBgBox.Text = _menu.ButtonHoverBackground;

        // Pin
        PinMinWidthBox.Text = _pin.MinWidth.ToString(CultureInfo.InvariantCulture);
        PinMinHeightBox.Text = _pin.MinHeight.ToString(CultureInfo.InvariantCulture);
        PinBorderColorBox.Text = _pin.BorderColor;
        PinShadowBlurBox.Text = _pin.ShadowBlurRadius.ToString(CultureInfo.InvariantCulture);
        PinRotationStepBox.Text = _pin.RotationStepDegrees.ToString(CultureInfo.InvariantCulture);
        PinDefaultOpacityBox.Text = _pin.DefaultOpacity.ToString(CultureInfo.InvariantCulture);
    }

    private void WireColorPreviews()
    {
        WireColorPreview(SnippingMaskColorBox, SnippingMaskColorPreview);
        WireColorPreview(SnippingBorderColorBox, SnippingBorderColorPreview);
        WireColorPreview(SnippingOverlayBgBox, SnippingOverlayBgPreview);
        WireColorPreview(MenuBackgroundBox, MenuBackgroundPreview);
        WireColorPreview(MenuButtonBgBox, MenuButtonBgPreview);
        WireColorPreview(MenuButtonHoverBgBox, MenuButtonHoverBgPreview);
        WireColorPreview(PinBorderColorBox, PinBorderColorPreview);
    }

    /// <summary>把 TextBox 的颜色串实时映射到预览色块；无效值清空预览。</summary>
    private static void WireColorPreview(System.Windows.Controls.TextBox box, System.Windows.Controls.Border preview)
    {
        UpdateColorPreview(box, preview);
        box.TextChanged += (s, e) => UpdateColorPreview(box, preview);
    }

    private static void UpdateColorPreview(System.Windows.Controls.TextBox box, System.Windows.Controls.Border preview)
    {
        try { preview.Background = BrushHelper.ToBrush(box.Text); }
        catch { preview.Background = null; }
    }

    private static int ToIndex(ActionSettings s)
    {
        if (s.WakeupMessage == ActionSettings.WAKEUP_CIRCLE_GESTURE)
            return 2; // 画圈
        if (s.WakeupMessage == NativeMethods.WM_XBUTTONDOWN)
            return 1; // 侧键后退 (XButton1)
        return 0; // 中键
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // Commit any in-flight cell edit before persisting.
        ActionsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);

        if (!Validate(out string error))
        {
            System.Windows.MessageBox.Show(error, "设置", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        // Action
        int index = WakeupKeyCombo.SelectedIndex;
        _action.WakeupMessage = index switch
        {
            1 => NativeMethods.WM_XBUTTONDOWN,
            2 => ActionSettings.WAKEUP_CIRCLE_GESTURE,
            _ => NativeMethods.WM_MBUTTONDOWN,
        };
        _action.XButtonData = index == 1 ? 1 : 0;

        // Snipping
        _snipping.DragThreshold = double.Parse(SnippingDragThresholdBox.Text, CultureInfo.InvariantCulture);
        _snipping.MaskColor = SnippingMaskColorBox.Text;
        _snipping.BorderColor = SnippingBorderColorBox.Text;
        _snipping.BorderThickness = int.Parse(SnippingBorderThicknessBox.Text, CultureInfo.InvariantCulture);
        _snipping.OverlayBackground = SnippingOverlayBgBox.Text;

        // Menu
        _menu.Width = double.Parse(MenuWidthBox.Text, CultureInfo.InvariantCulture);
        _menu.Height = double.Parse(MenuHeightBox.Text, CultureInfo.InvariantCulture);
        _menu.Background = MenuBackgroundBox.Text;
        _menu.CornerRadius = int.Parse(MenuCornerRadiusBox.Text, CultureInfo.InvariantCulture);
        _menu.ButtonBackground = MenuButtonBgBox.Text;
        _menu.ButtonHoverBackground = MenuButtonHoverBgBox.Text;

        // Pin
        _pin.MinWidth = double.Parse(PinMinWidthBox.Text, CultureInfo.InvariantCulture);
        _pin.MinHeight = double.Parse(PinMinHeightBox.Text, CultureInfo.InvariantCulture);
        _pin.BorderColor = PinBorderColorBox.Text;
        _pin.ShadowBlurRadius = double.Parse(PinShadowBlurBox.Text, CultureInfo.InvariantCulture);
        _pin.RotationStepDegrees = double.Parse(PinRotationStepBox.Text, CultureInfo.InvariantCulture);
        _pin.DefaultOpacity = double.Parse(PinDefaultOpacityBox.Text, CultureInfo.InvariantCulture);

        // Persist: reattach all four groups (a wake may have replaced Settings) then save.
        var s = SettingsManager.Instance.Settings;
        s.Action = _action;
        s.Snipping = _snipping;
        s.Menu = _menu;
        s.Pin = _pin;
        SettingsManager.Instance.Save();

        _hookService.UpdateSettings(_action);
        _mainWindow?.ApplyMenuSettings(_menu);
        Close();
    }

    /// <summary>校验全部数值与颜色字段；返回 false 时 error 给出提示。</summary>
    private bool Validate(out string error)
    {
        if (!double.TryParse(SnippingDragThresholdBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        { error = "“点击/拖拽阈值”需为数字。"; return false; }
        if (!int.TryParse(SnippingBorderThicknessBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        { error = "“红框厚度”需为整数。"; return false; }
        if (!double.TryParse(MenuWidthBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        { error = "“窗口宽度”需为数字。"; return false; }
        if (!double.TryParse(MenuHeightBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        { error = "“窗口高度”需为数字。"; return false; }
        if (!int.TryParse(MenuCornerRadiusBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        { error = "“圆角半径”需为整数。"; return false; }
        if (!double.TryParse(PinMinWidthBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        { error = "“最小宽度”需为数字。"; return false; }
        if (!double.TryParse(PinMinHeightBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        { error = "“最小高度”需为数字。"; return false; }
        if (!double.TryParse(PinShadowBlurBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        { error = "“阴影模糊半径”需为数字。"; return false; }
        if (!double.TryParse(PinRotationStepBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        { error = "“旋转步进”需为数字。"; return false; }
        if (!double.TryParse(PinDefaultOpacityBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double op) || op < 0 || op > 1)
        { error = "“默认不透明度”需为 0~1 的数字。"; return false; }

        if (!IsValidColor(SnippingMaskColorBox) || !IsValidColor(SnippingBorderColorBox) ||
            !IsValidColor(SnippingOverlayBgBox) || !IsValidColor(MenuBackgroundBox) ||
            !IsValidColor(MenuButtonBgBox) || !IsValidColor(MenuButtonHoverBgBox) ||
            !IsValidColor(PinBorderColorBox))
        { error = "颜色字段需为 #AARRGGBB 或命名色（如 Black）。"; return false; }

        error = string.Empty;
        return true;
    }

    private static bool IsValidColor(System.Windows.Controls.TextBox box)
    {
        try { BrushHelper.ToBrush(box.Text); return true; }
        catch { return false; }
    }
}
