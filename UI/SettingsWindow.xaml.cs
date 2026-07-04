using System.Globalization;
using System.Windows;
using MyQuicker.Interop;
using MyQuicker.Models;
using MyQuicker.Services;

namespace MyQuicker.UI;

/// <summary>
/// 设置中心：常规(唤醒键) / 动作管理 / 截屏与贴图 / 菜单 四页。
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

        // ActionStore.LoadForEdit() 返回内存缓存的深拷贝（隔离未保存编辑，无 IO）。
        _action = ActionStore.LoadForEdit();
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
        InterceptWakeupCheckBox.IsChecked = _action.InterceptWakeupKey;
        CircleSensitivityCombo.SelectedIndex = (int)_action.CircleSensitivity;
        ActionsGrid.ItemsSource = _action.Actions;

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
        _action.InterceptWakeupKey = InterceptWakeupCheckBox.IsChecked == true;
        _action.CircleSensitivity = (CircleSensitivity)CircleSensitivityCombo.SelectedIndex;

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

        // Persist: reattach all four groups then save.
        var s = SettingsManager.Instance.Settings;
        s.Action = _action;
        s.Snipping = _snipping;
        s.Menu = _menu;
        s.Pin = _pin;
        SettingsManager.Instance.Save();
        ActionStore.UpdateCache(_action); // 同步动作内存缓存（唤醒零 IO，docs/03 §7.4）

        _hookService.UpdateSettings(_action);
        _mainWindow?.ApplyMenuSettings(_menu);
        _mainWindow?.RefreshActions(); // 菜单动作列表即时刷新
        Close();
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
}
