using System.Windows;

namespace BladeControl.UI;

/// <summary>
/// A value pulled towards a target by a spring, stepped by the caller each
/// frame. It keeps its velocity, so a gesture's speed carries into it: a
/// released thumb flies on and settles, and a thumb that is moving when its
/// target changes curves to the new one instead of starting over.
/// </summary>
internal sealed class Spring
{
    /// <summary>How a spring moves, as stiffness and damping (for a mass of 1).</summary>
    public readonly record struct Feel(double Stiffness, double Damping)
    {
        /// <summary>
        /// A spring that looks settled after about <paramref name="seconds"/>,
        /// overshooting by <paramref name="bounce"/> (0 none, 1 forever).
        /// </summary>
        public static Feel Of(double seconds, double bounce)
        {
            var root = 2 * Math.PI / (seconds * 1.2);
            return new Feel(root * root, 2 * (1 - bounce) * root);
        }
    }

    /// <summary>Quick, with a touch of overshoot: thumbs going to their place.</summary>
    public static readonly Feel Snappy = Feel.Of(0.3, 0.15);

    /// <summary>Softer and bouncier: a thumb straining at a limit and coming back.</summary>
    public static readonly Feel Morph = Feel.Of(0.45, 0.3);

    private readonly double _restDistance;
    private readonly double _restSpeed;
    private Feel _feel = Snappy;

    /// <param name="position">Where it starts, at rest.</param>
    /// <param name="restDistance">How close to the target counts as there.</param>
    public Spring(double position, double restDistance)
    {
        Position = Target = position;
        _restDistance = restDistance;
        _restSpeed = restDistance * 20;
    }

    public double Position { get; private set; }

    /// <summary>Units per second.</summary>
    public double Velocity { get; private set; }

    public double Target { get; private set; }

    public bool Moving { get; private set; }

    /// <summary>Heads for <paramref name="target"/>, keeping its speed unless one is given; with animations off, jumps there.</summary>
    public void To(double target, Feel feel, double? velocity = null)
    {
        if (Motion.Reduced)
        {
            Jump(target);
            return;
        }

        Target = target;
        _feel = feel;

        if (velocity is { } speed)
            Velocity = speed;

        Moving = Position != target || Velocity != 0;
    }

    /// <summary>Puts it at <paramref name="position"/>, at rest.</summary>
    public void Jump(double position)
    {
        Position = Target = position;
        Velocity = 0;
        Moving = false;
    }

    /// <summary>Moves it on by <paramref name="seconds"/>; false once it has come to rest.</summary>
    public bool Step(double seconds)
    {
        if (!Moving)
            return false;

        // A few small steps keep a stiff spring steady at a slow frame rate.
        const int Substeps = 4;
        var step = seconds / Substeps;

        for (var index = 0; index < Substeps; index++)
        {
            var acceleration = -_feel.Stiffness * (Position - Target) - _feel.Damping * Velocity;
            Velocity += acceleration * step;
            Position += Velocity * step;
        }

        if (Math.Abs(Velocity) < _restSpeed && Math.Abs(Position - Target) < _restDistance)
            Jump(Target);

        return Moving;
    }
}

/// <summary>The sums behind the sliders' feel: how far past a limit they give, and where a fling lands.</summary>
internal static class SliderPhysics
{
    /// <summary>
    /// Rubber-band resistance past a limit: the further the pointer goes,
    /// the less the thumb follows, never more than <paramref name="limit"/>.
    /// </summary>
    public static double Rubber(double distance, double limit) =>
        Math.Sign(distance) * (1 - 1 / (Math.Abs(distance) * 0.55 / limit + 1)) * limit;

    /// <summary>How much further a release at <paramref name="velocity"/> (units per second) carries, decelerating like a scroll.</summary>
    public static double Project(double velocity) => velocity * 0.099;

    /// <summary>
    /// The speed of a drag from its last few points (time in ms, position),
    /// per second; none if the pointer stopped before letting go.
    /// </summary>
    public static double ReleaseVelocity(IReadOnlyList<(double Time, double Position)> points, double releaseTime)
    {
        if (points.Count < 2)
            return 0;

        var first = points[0];
        var last = points[^1];
        var elapsed = (last.Time - first.Time) / 1000;

        return elapsed > 0.008 && releaseTime - last.Time < 60 ? (last.Position - first.Position) / elapsed : 0;
    }
}

/// <summary>Whether Windows' "Animation effects" is off: then the springs give way to instant changes.</summary>
internal static class Motion
{
    public static bool Reduced => !SystemParameters.ClientAreaAnimation;
}
