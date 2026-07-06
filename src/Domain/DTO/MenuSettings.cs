namespace MyQuicker.Domain.DTO;

/// <summary>唤醒菜单外观参数。仅包含纯数据。</summary>
public sealed class MenuSettings
{
    /// <summary>菜单内容区宽度（DIP）。实际窗口会额外包含 24 DIP 阴影边距。</summary>
    public double Width { get; set; } = 250;

    /// <summary>菜单内容区高度（DIP）。实际窗口会额外包含 24 DIP 阴影边距。</summary>
    public double Height { get; set; } = 250;

    /// <summary>菜单外层半透明背景色（ARGB 十六进制）。</summary>
    public string Background { get; set; } = "#E6202020";

    /// <summary>菜单外层圆角半径（px）。</summary>
    public int CornerRadius { get; set; } = 16;

    /// <summary>动作按钮背景色。</summary>
    public string ButtonBackground { get; set; } = "#26FFFFFF";

    /// <summary>动作按钮悬停背景色。</summary>
    public string ButtonHoverBackground { get; set; } = "#38FFFFFF";

    /// <summary>唤醒菜单网格列数（2 或 3）。</summary>
    public int GridColumns { get; set; } = 3;
}
