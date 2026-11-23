namespace Paomian.Desktop;

/// <summary>与帧率无关的临界阻尼弹簧。独立于控件，便于验证减速、反向和边界。</summary>
internal sealed class ScrollMotion
{
    private const double Frequency = 22;
    public double Position { get; private set; }
    public double Target { get; private set; }
    public double Velocity { get; private set; }
    public bool IsActive { get; private set; }

    public void Reset(double position)
    {
        Position = Target = position; Velocity = 0; IsActive = false;
    }

    public void Push(double distance, double current, double maximum, double viewport)
    {
        // 反向输入立即丢弃旧方向的待滚距离，不要求用户先抵消长队列。
        if (!IsActive || Math.Sign(distance) != Math.Sign(Target - Position)) Reset(current);
        var limit = Math.Max(48, viewport * 2);
        Target = Math.Clamp(Math.Clamp(Target + distance, current - limit, current + limit), 0, Math.Max(0, maximum));
        IsActive = Math.Abs(Target - Position) > .1;
    }

    public void Rebase(double current, double maximum)
    {
        Target = Math.Clamp(current + (Target - Position), 0, Math.Max(0, maximum));
        Position = current;
    }

    public double Advance(double seconds, double maximum)
    {
        Target = Math.Clamp(Target, 0, Math.Max(0, maximum));
        if (!IsActive || seconds <= 0) return Position;
        var t = Math.Min(seconds, .05);
        var displacement = Position - Target;
        var coefficient = Velocity + Frequency * displacement;
        var decay = Math.Exp(-Frequency * t);
        var next = Target + (displacement + coefficient * t) * decay;
        Velocity = (Velocity - Frequency * coefficient * t) * decay;
        // 不越过目标和内容边界，避免边缘露白或续载时反弹。
        Position = Math.Clamp(Math.Clamp(next, Math.Min(Position, Target), Math.Max(Position, Target)), 0, Math.Max(0, maximum));
        if (Math.Abs(Target - Position) < .25 && Math.Abs(Velocity) < 5)
        {
            Position = Target; Velocity = 0; IsActive = false;
        }
        return Position;
    }
}
