using Paomian.Core;

namespace Paomian.Desktop;

/// <summary>只记录尚未交给视口的相对距离，布局重排不会改变滚轮的目标行程。</summary>
internal sealed class ShortWheelMotion
{
    internal WheelTuning Tuning { get; set; } = WheelTuning.Recommended;
    private double _segment;
    private bool _repeatedSoftStart;
    internal double Remaining { get; private set; }

    internal double Push(double distance, double offset, double maximum, bool direct)
    {
        if (direct)
        {
            var immediate = Math.Clamp(offset + distance, 0, maximum) - offset;
            if (Math.Sign(Remaining) != Math.Sign(distance)) Reset();
            else if (Remaining != 0)
            {
                // 混合整格与精细事件时保留前一格的截止时间；精细包只移动自身的距离。
                var after = offset + immediate;
                var retained = Math.Clamp(after + Remaining, 0, maximum) - after;
                _segment *= retained / Remaining;
                Remaining = retained;
            }
            return immediate;
        }
        var carry = Math.Sign(Remaining) == Math.Sign(distance) ? Remaining : 0;
        var total = Math.Clamp(offset + carry + distance, 0, maximum) - offset;
        // 首段随输入提交；同向重复输入只增加本次行程，剩余部分共用一个截止时间。
        var first = Math.CopySign(Math.Min(Math.Abs(total), Math.Abs(distance) * Tuning.ImmediateFraction), total);
        // 连续输入已经在移动，沿用短过渡，避免反复重启接近 200 ms 的缓入段。
        _repeatedSoftStart = carry != 0 && Tuning.Curve == WheelCurve.SmoothStartStop;
        _segment = Remaining = total - first;
        return first;
    }

    internal double Advance(double elapsedMs)
    {
        var duration = _repeatedSoftStart ? Math.Min(Tuning.DurationMs, WheelTuning.Balanced.DurationMs) : Tuning.DurationMs;
        var t = Math.Clamp(1 - elapsedMs / duration, 0, 1);
        var next = Tuning.Curve switch
        {
            WheelCurve.Linear => _segment * t,
            WheelCurve.Cubic => _segment * t * t * t,
            // 五次平滑阶跃的对称余量。两端速度为零，有限时间内单调到达同一目标。
            WheelCurve.SmoothStartStop when !_repeatedSoftStart => _segment * (t * t * t * (10 + t * (6 * t - 15))),
            _ => _segment * t * t
        };
        var step = Remaining - next;
        Remaining = next;
        return step;
    }

    internal void Reset() => _segment = Remaining = 0;
}
