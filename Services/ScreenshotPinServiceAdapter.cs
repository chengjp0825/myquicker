using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Threading;
using MyQuicker.Domain.DTO;
using MyQuicker.Domain.Runtime;
using MyQuicker.UI;

namespace MyQuicker.Services;

/// <summary>
/// <see cref="IScreenshotPinService"/> 的 WPF 适配：用 <see cref="PinWindow"/> 钉住截图。
/// 构造时捕获 UI Dispatcher，确保窗口创建在 UI 线程执行。
/// </summary>
internal sealed class ScreenshotPinServiceAdapter : IScreenshotPinService
{
    private readonly PinSettings _pinSettings;
    private readonly Dispatcher _dispatcher;

    public ScreenshotPinServiceAdapter(PinSettings pinSettings)
    {
        _pinSettings = pinSettings ?? throw new System.ArgumentNullException(nameof(pinSettings));
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    /// <inheritdoc/>
    public Task PinAsync(Bitmap source, Rectangle physicalBounds)
    {
        return _dispatcher.InvokeAsync(() =>
        {
            try
            {
                var bitmapSource = BitmapSourceHelper.FromBitmap(source);
                var (scaleX, scaleY) = DpiHelper.ScaleForBounds(physicalBounds);
                var window = new PinWindow(
                    bitmapSource,
                    physicalBounds.X,
                    physicalBounds.Y,
                    scaleX,
                    scaleY,
                    _pinSettings);
                window.Show();
            }
            finally
            {
                source?.Dispose();
            }
        }).Task;
    }
}
