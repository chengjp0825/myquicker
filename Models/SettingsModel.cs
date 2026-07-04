using System.Collections.Generic;
using MyQuicker.Interop;

namespace MyQuicker.Models;

/// <summary>
/// 统一配置模型：动作/唤醒键 + 截屏 + 菜单 + 贴图四组参数。由
/// <see cref="MyQuicker.Services.SettingsManager"/> 单例持久化到 settings.json。
/// 默认值与重构前硬编码逐一对应，确保行为不变。Per SPEC 重构。
/// </summary>
public class SettingsModel
{
    /// <summary>唤醒键与动作列表（原 AppSettings 字段折叠于此）。</summary>
    public ActionSettings Action { get; set; } = new();

    /// <summary>截屏覆盖层参数。</summary>
    public SnippingSettings Snipping { get; set; } = new();

    /// <summary>唤醒菜单参数。</summary>
    public MenuSettings Menu { get; set; } = new();

    /// <summary>贴图窗口参数。</summary>
    public PinSettings Pin { get; set; } = new();
}

/// <summary>画圈手势灵敏度预设。低=不易误触，高=易触发。仅 WakeupMessage=画圈时生效。Per SPEC §4.1。</summary>
public enum CircleSensitivity
{
    /// <summary>不易误触：更大最小圈、更严闭合度、更短时间窗。</summary>
    Low = 0,

    /// <summary>默认平衡值。</summary>
    Medium = 1,

    /// <summary>易触发：更小最小圈、更宽闭合度、更长时间窗。</summary>
    High = 2,
}

/// <summary>唤醒键与动作列表配置（原 AppSettings 字段折叠于此）。</summary>
public class ActionSettings
{
    /// <summary>特殊唤醒方式：纯轨迹画圈（无按键），由 GlobalHookService 的 WM_MOUSEMOVE 旁观分支识别。</summary>
    public const int WAKEUP_CIRCLE_GESTURE = -1;

    /// <summary>唤醒菜单的鼠标消息（WM_MBUTTONDOWN / WM_XBUTTONDOWN / WAKEUP_CIRCLE_GESTURE）。</summary>
    public int WakeupMessage { get; set; } = NativeMethods.WM_MBUTTONDOWN;

    /// <summary>WM_XBUTTONDOWN 的侧键标识（1=后退/XBUTTON1, 2=前进/XBUTTON2）；中键/画圈时忽略。</summary>
    public int XButtonData { get; set; } = 0;

    /// <summary>唤醒时是否拦截（吞掉）唤醒键，不传递给当前前台应用。默认 true。Per SPEC §4.1。</summary>
    public bool InterceptWakeupKey { get; set; } = true;

    /// <summary>纯轨迹画圈手势的灵敏度预设。仅 WakeupMessage=画圈 时生效。Per SPEC §4.1。</summary>
    public CircleSensitivity CircleSensitivity { get; set; } = CircleSensitivity.Medium;

    /// <summary>用户自定义动作列表。</summary>
    public List<ActionItem> Actions { get; set; } = new();
}

/// <summary>截图结算后的动作。Per SPEC 8B。</summary>
public enum SnippingAfterScreenshot
{
    /// <summary>钉为贴图并复制到剪贴板（默认）。</summary>
    PinAndCopy = 0,

    /// <summary>仅复制到剪贴板，不钉贴图。</summary>
    CopyOnly = 1,

    /// <summary>仅钉为贴图，不写剪贴板。</summary>
    PinOnly = 2,
}

/// <summary>截图范围。Per SPEC 8B。</summary>
public enum SnippingCaptureScope
{
    /// <summary>所有显示器（跨屏拼接，默认）。</summary>
    AllMonitors = 0,

    /// <summary>仅当前光标所在显示器。</summary>
    CurrentMonitor = 1,
}

/// <summary>截屏覆盖层参数。Per SPEC 8B。</summary>
public class SnippingSettings
{
    /// <summary>判定"点击 vs 拖拽"的位移阈值（DIP）。超过即升级为拖拽。</summary>
    public double DragThreshold { get; set; } = 5.0;

    /// <summary>暗罩浓度（0~1，越大越暗）。暗罩恒为黑色，仅透明度可配（原 MaskColor ARGB 简化）。</summary>
    public double MaskAlpha { get; set; } = 0.4;

    /// <summary>选区寻边红框颜色。</summary>
    public string BorderColor { get; set; } = "#FF0000";

    // 红框厚度（2px）与覆盖层背景色（Black）已硬编码到 ScreenshotWindow，不再可配——
    // 默认值对几乎所有用户都正确，暴露后仅增加噪音。

    /// <summary>截图结算后的动作（钉贴图 / 写剪贴板 / 两者）。Per SPEC 8B。</summary>
    public SnippingAfterScreenshot AfterScreenshot { get; set; } = SnippingAfterScreenshot.PinAndCopy;

    /// <summary>截图范围：所有显示器（跨屏拼接）/ 当前光标所在显示器。Per SPEC 8B。</summary>
    public SnippingCaptureScope CaptureScope { get; set; } = SnippingCaptureScope.AllMonitors;
}

/// <summary>唤醒菜单参数。</summary>
public class MenuSettings
{
    /// <summary>菜单窗口宽度（DIP）。</summary>
    public double Width { get; set; } = 250;

    /// <summary>菜单窗口高度（DIP）。</summary>
    public double Height { get; set; } = 250;

    /// <summary>菜单外层半透明背景色（ARGB 十六进制）。</summary>
    public string Background { get; set; } = "#E6202020";

    /// <summary>菜单外层圆角半径（px）。</summary>
    public int CornerRadius { get; set; } = 16;

    /// <summary>动作按钮背景色。</summary>
    public string ButtonBackground { get; set; } = "#26FFFFFF";

    /// <summary>动作按钮悬停背景色。</summary>
    public string ButtonHoverBackground { get; set; } = "#38FFFFFF";
}

/// <summary>贴图窗口参数。Per SPEC 8C。</summary>
public class PinSettings
{
    // 最小宽高（40×40）、阴影模糊半径（14）、旋转步进（90°）已硬编码到 PinWindow，不再可配——
    // 默认值对几乎所有用户都正确；且旋转步进非 90° 会破坏 90/270 宽高互换逻辑（footgun）。

    /// <summary>"显示边界"开启时的边框颜色。</summary>
    public string BorderColor { get; set; } = "Gray";

    /// <summary>贴图默认不透明度。</summary>
    public double DefaultOpacity { get; set; } = 1.0;

    /// <summary>贴图默认是否显示边界（钉图时默认开启 2px 边框，向外生长）。</summary>
    public bool DefaultShowBorder { get; set; } = true;

    /// <summary>贴图默认是否开启批注模式（Hover 工具栏）。默认关闭。</summary>
    public bool DefaultAnnotationMode { get; set; } = false;

    /// <summary>贴图默认是否置顶。默认开启。Per SPEC 8C。</summary>
    public bool DefaultTopmost { get; set; } = true;

    /// <summary>贴图默认是否显示阴影。默认开启。Per SPEC 8C。</summary>
    public bool DefaultShowShadow { get; set; } = true;
}
