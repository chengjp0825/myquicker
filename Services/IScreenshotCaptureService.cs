namespace MyQuicker.Services;

/// <summary>
/// 截图采集服务接口：返回持有非托管 GDI 资源的 <see cref="CapturedImage"/>，
/// 调用方负责在转换/消费完毕后显式释放。
/// </summary>
public interface IScreenshotCaptureService
{
    /// <summary>按当前设置采集屏幕，返回可释放的截图实体。</summary>
    CapturedImage Capture();

    /// <summary>
    /// 异步采集屏幕，避免 UI 线程被 <see cref="System.Drawing.Graphics.CopyFromScreen"/> 阻塞。
    /// 调用方仍需在消费完毕后显式释放返回的 <see cref="CapturedImage"/ >。
    /// </summary>
    Task<CapturedImage> CaptureAsync();
}
