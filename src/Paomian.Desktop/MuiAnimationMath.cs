/*
 * Animation parameters adapted from MUI v9.3.1 CircularProgress.js and LinearProgress.js.
 * Copyright (c) 2014 Call-Em-All. See licenses/MUI-MIT.txt.
 */
namespace Paomian.Desktop;

internal readonly record struct MuiCircularFrame(double DashLength, double DashOffset, double RotationDegrees);
internal readonly record struct MuiLinearFrame(double Left, double Right);

internal static class MuiAnimationMath
{
    internal static MuiCircularFrame CircularFrame(double phase, bool reducedMotion = false)
    {
        if (reducedMotion)
            return new MuiCircularFrame(80, 0, 0);

        phase = Math.Clamp(phase, 0, 1);
        if (phase <= .5)
        {
            var amount = CubicBezier(phase * 2, .42, 0, .58, 1);
            return new MuiCircularFrame(1 + 99 * amount, -15 * amount, phase * 360);
        }

        var shrink = CubicBezier((phase - .5) * 2, .42, 0, .58, 1);
        return new MuiCircularFrame(100 - 99 * shrink, -15 - 111 * shrink, phase * 360);
    }

    internal static MuiLinearFrame FirstLinearSegment(double phase) =>
        LinearSegment(phase, -.35, 1, 1, -.90, .65, .815, .735, .395);

    internal static MuiLinearFrame SecondLinearSegment(double phase) =>
        LinearSegment(phase, -2, 1, 1.07, -.08, .165, .84, .44, 1);

    internal static double CubicBezier(double x, double x1, double y1, double x2, double y2)
    {
        x = Math.Clamp(x, 0, 1);
        var lower = 0d;
        var upper = 1d;
        var parameter = x;
        for (var iteration = 0; iteration < 24; iteration++)
        {
            var estimate = CubicCoordinate(parameter, x1, x2);
            if (estimate < x) lower = parameter;
            else upper = parameter;
            parameter = (lower + upper) / 2;
        }

        return CubicCoordinate(parameter, y1, y2);
    }

    private static MuiLinearFrame LinearSegment(
        double phase, double initialLeft, double initialRight, double finalLeft, double finalRight,
        double x1, double y1, double x2, double y2)
    {
        phase = Math.Clamp(phase, 0, 1);
        var progress = phase >= .6 ? 1 : CubicBezier(phase / .6, x1, y1, x2, y2);
        return new MuiLinearFrame(
            initialLeft + (finalLeft - initialLeft) * progress,
            initialRight + (finalRight - initialRight) * progress);
    }

    private static double CubicCoordinate(double t, double first, double second)
    {
        var inverse = 1 - t;
        return 3 * inverse * inverse * t * first + 3 * inverse * t * t * second + t * t * t;
    }
}
