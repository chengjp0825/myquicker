using System.Windows.Media;

namespace MyQuicker.UI;

/// <summary>
/// JSON 颜色串（"#AARRGGBB" 或命名色如 "Red"/"Gray"/"Black"）转 WPF <see cref="Brush"/ > 的集中转换器。
/// 复用单个 <see cref="BrushConverter"/ >，供各窗口 code-behind 从 Settings 赋值时调用。
/// Per SPEC 重构（Step 3）。
/// </summary>
public static class BrushHelper
{
    private static readonly BrushConverter Converter = new();

    /// <summary>把 JSON 颜色串转为 WPF Brush。无效值抛 ArgumentException。</summary>
    public static System.Windows.Media.Brush ToBrush(string value)
    {
        object? result = Converter.ConvertFromString(value);
        return Freeze((System.Windows.Media.Brush)(result ?? throw new ArgumentException($"无效的颜色值: {value}", nameof(value))));
    }

    /// <summary>
    /// 尝试把 JSON 颜色串转为 WPF Brush。成功返回 true 并输出冻结的 Brush；
    /// 失败返回 false，brush 为 null。
    /// </summary>
    public static bool TryToBrush(string value, out System.Windows.Media.Brush? brush)
    {
        try
        {
            brush = ToBrush(value);
            return true;
        }
        catch
        {
            brush = null;
            return false;
        }
    }

    /// <summary>
    /// 安全地把 JSON 颜色串转为 WPF Brush；解析失败时返回 <paramref name="fallback"/ >。
    /// 返回的 Brush 尽可能被冻结，便于跨线程共享与 GC 回收。
    /// </summary>
    public static System.Windows.Media.Brush SafeToBrush(string value, System.Windows.Media.Brush fallback)
    {
        if (TryToBrush(value, out var brush) && brush is not null)
            return brush;

        return Freeze(fallback);
    }

    private static System.Windows.Media.Brush Freeze(System.Windows.Media.Brush brush)
    {
        if (brush.CanFreeze && !brush.IsFrozen)
            brush.Freeze();

        return brush;
    }
}
