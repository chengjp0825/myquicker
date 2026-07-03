using System.Windows.Media;

namespace MyQuicker.UI;

/// <summary>
/// JSON 颜色串（"#AARRGGBB" 或命名色如 "Red"/"Gray"/"Black"）转 WPF <see cref="Brush"/> 的集中转换器。
/// 复用单个 <see cref="BrushConverter"/>，供各窗口 code-behind 从 SettingsModel 赋值时调用。
/// Per SPEC 重构（Step 3）。
/// </summary>
public static class BrushHelper
{
    private static readonly BrushConverter Converter = new();

    /// <summary>把 JSON 颜色串转为 WPF Brush。无效值抛 ArgumentException。</summary>
    public static System.Windows.Media.Brush ToBrush(string value)
    {
        object? result = Converter.ConvertFromString(value);
        return (System.Windows.Media.Brush)(result ?? throw new ArgumentException($"无效的颜色值: {value}", nameof(value)));
    }
}
