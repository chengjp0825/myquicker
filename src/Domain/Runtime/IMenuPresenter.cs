using System;

namespace MyQuicker.Domain.Runtime;

/// <summary>菜单表现层抽象：WakeOrchestrator 通过此接口控制 WPF 窗口。</summary>
public interface IMenuPresenter
{
    /// <summary>在指定 DIP 位置显示菜单。</summary>
    void ShowAt(Point location);

    /// <summary>关闭菜单。仅由 <see cref="WakeOrchestrator"/> 调用。</summary>
    void Dismiss();

    /// <summary>菜单是否处于可见状态（含动画中）。</summary>
    bool IsVisible { get; }

    /// <summary>菜单请求关闭（例如用户点击外部、点击按钮或齿轮）。</summary>
    /// <remarks>
    /// 由 <see cref="WakeOrchestrator"/> 订阅并处理；Presenter 自身不得直接调用 <see cref="Dismiss"/>。
    /// </remarks>
    event EventHandler? DismissRequested;

    event EventHandler? Opened;
    event EventHandler? Closed;
}
