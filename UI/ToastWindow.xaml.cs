using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MyQuicker.UI;

/// <summary>
/// 轻量瞬时 toast 通知窗口：无框置顶、<c>ShowActivated=False</c> 不抢焦点、
/// 右下角堆叠（多个 toast 向上叠）、淡入 / 定时淡出自动关闭。
/// 通过 <see cref="Toast.Show"/> 静态调用，调用方无需管生命周期。
/// Per docs/03 §8。
/// </summary>
public partial class ToastWindow : Window
{
    /// <summary>当前显示中的 toast，用于堆叠定位与避免 GC 回收（窗口 Close 后移除）。</summary>
    private static readonly List<ToastWindow> _active = new();

    private readonly int _durationMs;

    internal ToastWindow(string message, int durationMs)
    {
        InitializeComponent();
        MessageText.Text = message;
        _durationMs = durationMs;
        Loaded += ToastWindow_Loaded;
        Closed += ToastWindow_Closed;
        _active.Add(this);
    }

    private void ToastWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 定位：主屏工作区右下角；多个 toast 向上堆叠（贴着已有 toast 上方）。
        const double margin = 16;
        const double gap = 10;
        double workRight = SystemParameters.WorkArea.Right;
        double workBottom = SystemParameters.WorkArea.Bottom;
        double top = workBottom - ActualHeight - margin;
        foreach (var t in _active)
        {
            if (t != this && t.IsLoaded)
                top = Math.Min(top, t.Top - gap - ActualHeight);
        }
        Left = workRight - ActualWidth - margin;
        Top = top;

        // 淡入 0→1（150ms）。
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
        BeginAnimation(OpacityProperty, fadeIn);

        // 定时淡出 1→0（200ms）后 Close。
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_durationMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fadeOut);
        };
        timer.Start();
    }

    private void ToastWindow_Closed(object? sender, EventArgs e)
    {
        _active.Remove(this);
    }
}
