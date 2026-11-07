using System.Windows;
using System.Windows.Media.Animation;

namespace FovApp.Services;

/// <summary>
/// Motion helpers — mirrors SnapHook. Anything that moves checks ReduceMotion
/// so a system that asks for reduced motion gets instant changes only.
/// </summary>
public static class Motion
{
    private static bool? _reduceMotion;

    public static bool ReduceMotion => _reduceMotion ??= Detect();

    public static int Move(int milliseconds) => ReduceMotion ? Math.Min(milliseconds, 100) : milliseconds;
    public static int Fade(int milliseconds) => ReduceMotion ? Math.Min(milliseconds, 100) : milliseconds;

    public static DoubleAnimation Value(double to, int ms, EasingMode mode = EasingMode.EaseOut) =>
        new(to, TimeSpan.FromMilliseconds(ReduceMotion ? Math.Min(ms, 100) : ms))
        {
            EasingFunction = new CubicEase { EasingMode = mode }
        };

    public static DoubleAnimation Value(double from, double to, int ms, EasingMode mode = EasingMode.EaseOut) =>
        new(from, to, TimeSpan.FromMilliseconds(ReduceMotion ? Math.Min(ms, 100) : ms))
        {
            EasingFunction = new CubicEase { EasingMode = mode }
        };

    private static bool Detect()
    {
        try { return !SystemParameters.ClientAreaAnimation; }
        catch { return false; }
    }
}
