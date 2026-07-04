using System;
using System.Collections.Generic;
using MyQuicker.Interop;
using static MyQuicker.Interop.NativeMethods;

namespace MyQuicker.Services;

/// <summary>
/// 纯轨迹画圈识别：对滑窗内的鼠标轨迹做几何判定，无按键即可触发唤醒。
/// 调用方（GlobalHookService）负责维护 ≤800ms 的滑动时间窗并剔除老点；
/// 本方法只做空间几何判定，O(n) 单趟扫描，n≈50~120 点时开销可忽略。
/// </summary>
internal static class GestureHelper
{
    // —— 阈值（防误触）——
    private const double MinBoxSide      = 80.0;   // 外接矩形最小边：排除微小抖动
    private const double MinAspect       = 0.5;    // 宽高比下限
    private const double MaxAspect       = 2.0;    // 宽高比上限（过窄长条不算圆）
    private const double MinTotalTurnDeg = 300.0;  // 累计有符号偏转角阈值（≈5π/6，接近一圈）
    private const double MinVectorLen    = 2.0;    // 过滤亚像素抖动的最小向量长度
    private const int    MinPoints       = 8;      // 最少样本点

    /// <summary>
    /// 判定近期轨迹是否构成"快速画出的一个圆"。true=识别为圆圈唤醒手势。
    /// </summary>
    public static bool IsCircle(List<POINT> recentPoints)
    {
        if (recentPoints is null || recentPoints.Count < MinPoints)
            return false;

        // 1) Bounding Box：宽高均 ≥80px，宽高比 0.5~2.0
        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;
        for (int i = 0; i < recentPoints.Count; i++)
        {
            var p = recentPoints[i];
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }
        double width  = maxX - minX;
        double height = maxY - minY;
        if (width < MinBoxSide || height < MinBoxSide)
            return false;
        double aspect = width / height;
        if (aspect < MinAspect || aspect > MaxAspect)
            return false;

        // 2) 有符号偏转角累加（转向数 / turning number）。
        //    一致方向画一圈 ≈ ±360°；直线/折线 ≈ 0°；来回折返正负抵消 ≈ 0°。
        //    取总数的绝对值 ≥300° 即判定为近闭合圆。
        double totalTurn = 0.0;
        double prevAngle = double.NaN;
        for (int i = 1; i < recentPoints.Count; i++)
        {
            double dx = recentPoints[i].X - recentPoints[i - 1].X;
            double dy = recentPoints[i].Y - recentPoints[i - 1].Y;
            if (dx * dx + dy * dy < MinVectorLen * MinVectorLen)
                continue;                          // 跳过抖动，prevAngle 不更新

            double angle = Math.Atan2(dy, dx);
            if (!double.IsNaN(prevAngle))
            {
                double delta = angle - prevAngle;
                // 归一化到 [-π, π]，避免跨象限跳变累加错误
                if (delta > Math.PI) delta -= 2 * Math.PI;
                else if (delta < -Math.PI) delta += 2 * Math.PI;
                totalTurn += delta;
            }
            prevAngle = angle;
        }

        return Math.Abs(totalTurn) * (180.0 / Math.PI) >= MinTotalTurnDeg;
    }
}
