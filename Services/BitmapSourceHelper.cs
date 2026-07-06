using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using MyQuicker.Interop;

namespace MyQuicker.Services;

/// <summary>
/// GDI+ <see cref="Bitmap"/> 与 WPF <see cref="BitmapSource"/> 之间的转换辅助。
/// 仅由 WPF 适配器使用，保持领域层无 WPF 依赖。
/// </summary>
internal static class BitmapSourceHelper
{
    /// <summary>
    /// 把 GDI+ Bitmap 转换为冻结的 WPF BitmapSource；转换后立即释放临时 HBITMAP。
    /// </summary>
    public static BitmapSource FromBitmap(Bitmap bitmap)
    {
        if (bitmap is null)
            throw new System.ArgumentNullException(nameof(bitmap));

        IntPtr hBitmap = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            NativeMethods.DeleteObject(hBitmap);
        }
    }
}
