using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Threading;
using MyQuicker.Domain.DTO;
using MyQuicker.Domain.Runtime;
using MyQuicker.UI;

namespace MyQuicker.Services;

/// <summary>
/// <see cref="IScreenshotOverlay"/> 的 WPF 适配：用 <see cref="ScreenshotWindow"/> 完成区域选择。
/// 构造时捕获 UI Dispatcher，确保 ShowDialog 在 UI 线程执行。
/// </summary>
internal sealed class ScreenshotOverlayAdapter : IScreenshotOverlay
{
    private readonly SnippingSettings _snippingSettings;
    private readonly Dispatcher _dispatcher;

    public ScreenshotOverlayAdapter(SnippingSettings snippingSettings)
    {
        _snippingSettings = snippingSettings ?? throw new System.ArgumentNullException(nameof(snippingSettings));
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    /// <inheritdoc/>
    public Task<Rectangle?> SelectRegionAsync(Bitmap fullImage, Rectangle fullBounds)
    {
        return _dispatcher.InvokeAsync(() =>
        {
            var source = BitmapSourceHelper.FromBitmap(fullImage);
            var window = new ScreenshotWindow(source, fullBounds, _snippingSettings);
            bool? result = window.ShowDialog();
            if (result == true && window.SelectedBounds.HasValue)
                return window.SelectedBounds.Value;
            return (Rectangle?)null;
        }).Task;
    }
}
