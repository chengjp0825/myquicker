using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Threading.Tasks;
using MyQuicker.Domain.DTO;
using MyQuicker.Services;

namespace MyQuicker.Domain.Runtime;

/// <summary>
/// 截图工作流编排器：Capture → Select Region → Pin。
/// 把原先散落在 <see cref="UI.MainWindow"/> 与 <see cref="UI.ScreenshotWindow"/> 中的流程收敛到可测试的单一入口。
/// 本类仅依赖 GDI+，不引用任何 WPF 类型，保证领域层与 UI 框架解耦。
/// </summary>
public class ScreenshotWorkflow
{
    private readonly IScreenshotCaptureService _captureService;
    private readonly IScreenshotOverlay _overlay;
    private readonly IScreenshotPinService _pinService;
    private readonly IToastService _toastService;
    private readonly SnippingSettings _snippingSettings;

    /// <summary>初始化工作流及其所有依赖。</summary>
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
        _ = pinSettings ?? throw new ArgumentNullException(nameof(pinSettings));
    }

    /// <summary>执行完整截图工作流。</summary>
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

            Rectangle? selection = await _overlay.SelectRegionAsync(captured.Bitmap, captured.Bounds).ConfigureAwait(false);
            if (!selection.HasValue)
                return;

            cancellationToken.ThrowIfCancellationRequested();

            Rectangle selectedBounds = selection.Value;
            Rectangle cropRect = new Rectangle(
                selectedBounds.X - captured.Bounds.X,
                selectedBounds.Y - captured.Bounds.Y,
                selectedBounds.Width,
                selectedBounds.Height);

            // 严格夹取到 base-image 边界，防止越界矩形让 DrawImage 抛异常。
            cropRect.X = Math.Clamp(cropRect.X, 0, captured.Bitmap.Width);
            cropRect.Y = Math.Clamp(cropRect.Y, 0, captured.Bitmap.Height);
            cropRect.Width = Math.Clamp(cropRect.Width, 0, captured.Bitmap.Width - cropRect.X);
            cropRect.Height = Math.Clamp(cropRect.Height, 0, captured.Bitmap.Height - cropRect.Y);

            if (cropRect.Width <= 0 || cropRect.Height <= 0)
                return;

            Bitmap crop = CropBitmap(captured.Bitmap, cropRect);
            await _pinService.PinAsync(crop, selectedBounds).ConfigureAwait(false);
            // 注意：crop Bitmap 的所有权转移给 _pinService 的实现；适配器在转换为 BitmapSource 后负责释放。
        }
        finally
        {
            captured.Dispose();
        }
    }

    /// <summary>用 GDI+ 从源位图中裁剪出指定矩形区域。</summary>
    private static Bitmap CropBitmap(Bitmap source, Rectangle cropRect)
    {
        var crop = new Bitmap(cropRect.Width, cropRect.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(crop))
        {
            g.DrawImage(
                source,
                new Rectangle(0, 0, cropRect.Width, cropRect.Height),
                cropRect,
                GraphicsUnit.Pixel);
        }

        return crop;
    }
}
