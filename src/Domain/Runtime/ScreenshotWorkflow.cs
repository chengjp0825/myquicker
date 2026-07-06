using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MyQuicker.Domain.DTO;
using MyQuicker.Interop;
using MyQuicker.Services;

namespace MyQuicker.Domain.Runtime;

/// <summary>
/// 截图工作流编排器：Capture → Select Region → Pin。
/// 把原先散落在 <see cref="UI.MainWindow"/> 与 <see cref="UI.ScreenshotWindow"/> 中的流程收敛到可测试的单一入口。
/// </summary>
public class ScreenshotWorkflow
{
    private readonly IScreenshotCaptureService _captureService;
    private readonly IScreenshotOverlay _overlay;
    private readonly IScreenshotPinService _pinService;
    private readonly IToastService _toastService;
    private readonly SnippingSettings _snippingSettings;
    private readonly PinSettings _pinSettings;
    private readonly Dispatcher? _uiDispatcher;

    /// <summary>
    /// 初始化工作流及其所有依赖。构造时捕获当前线程的 Dispatcher，供剪贴板操作调度回 UI 线程。
    /// </summary>
    public ScreenshotWorkflow(
        IScreenshotCaptureService captureService,
        IScreenshotOverlay overlay,
        IScreenshotPinService pinService,
        IToastService toastService,
        SnippingSettings snippingSettings,
        PinSettings pinSettings)
    {
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _pinService = pinService ?? throw new ArgumentNullException(nameof(pinService));
        _toastService = toastService ?? throw new ArgumentNullException(nameof(toastService));
        _snippingSettings = snippingSettings ?? throw new ArgumentNullException(nameof(snippingSettings));
        _pinSettings = pinSettings ?? throw new ArgumentNullException(nameof(pinSettings));
        _uiDispatcher = Dispatcher.CurrentDispatcher;
    }

    /// <summary>
    /// 执行完整截图工作流。
    /// </summary>
    /// <param name="cancellationToken">取消令牌；在步骤边界处检查。</param>
    public virtual async Task RunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CapturedImage captured;
        try
        {
            captured = await _captureService.CaptureAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _toastService.Show($"截图失败：{ex.Message}", 3000);
            return;
        }

        if (captured?.Bitmap is null)
        {
            _toastService.Show("截图失败", 3000);
            return;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            BitmapSource source = await Task.Run(() => ConvertToBitmapSource(captured.Bitmap)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            Rectangle? selection = await _overlay.SelectRegionAsync(source, captured.Bounds).ConfigureAwait(false);
            if (!selection.HasValue)
                return;

            cancellationToken.ThrowIfCancellationRequested();

            Rectangle selectedBounds = selection.Value;
            Rectangle cropRect = new Rectangle(
                selectedBounds.X - captured.Bounds.X,
                selectedBounds.Y - captured.Bounds.Y,
                selectedBounds.Width,
                selectedBounds.Height);

            // 严格夹取到 base-image 边界，防止越界矩形让 CroppedBitmap 抛异常。
            cropRect.X = Math.Clamp(cropRect.X, 0, source.PixelWidth);
            cropRect.Y = Math.Clamp(cropRect.Y, 0, source.PixelHeight);
            cropRect.Width = Math.Clamp(cropRect.Width, 0, source.PixelWidth - cropRect.X);
            cropRect.Height = Math.Clamp(cropRect.Height, 0, source.PixelHeight - cropRect.Y);

            if (cropRect.Width <= 0 || cropRect.Height <= 0)
                return;

            var crop = new CroppedBitmap(source, new Int32Rect(cropRect.X, cropRect.Y, cropRect.Width, cropRect.Height));
            crop.Freeze();

            if (_snippingSettings.AfterScreenshot != SnippingAfterScreenshot.PinOnly)
            {
                await CopyToClipboardAsync(crop).ConfigureAwait(false);
            }

            if (_snippingSettings.AfterScreenshot != SnippingAfterScreenshot.CopyOnly)
            {
                await _pinService.PinAsync(crop, selectedBounds).ConfigureAwait(false);
            }
        }
        finally
        {
            captured.Dispose();
        }
    }

    /// <summary>
    /// 把 GDI+ Bitmap 转换为冻结的 WPF BitmapSource；转换后立即释放临时 HBITMAP。
    /// </summary>
    private static BitmapSource ConvertToBitmapSource(Bitmap bitmap)
    {
        if (bitmap is null)
            throw new ArgumentNullException(nameof(bitmap));

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

    /// <summary>
    /// 将裁剪后的图片写入剪贴板；被独占时仅弹 toast 不阻断流程。
    /// 必须在 UI 线程调用，因此通过构造时捕获的 Dispatcher 调度。
    /// </summary>
    private async Task CopyToClipboardAsync(BitmapSource crop)
    {
        if (_uiDispatcher is null)
            return;

        try
        {
            await _uiDispatcher.InvokeAsync(() => System.Windows.Clipboard.SetImage(crop));
        }
        catch (Exception ex)
        {
            _toastService.Show($"剪贴板被占用：{ex.Message}", 3000);
        }
    }
}
