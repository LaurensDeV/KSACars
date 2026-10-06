using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// One collider of a structure standing on the ground: a box, or a cylinder along its own Y, which is
/// how KSA declares both. The axes are the solid's own, written in the structure's frame.
/// </summary>
/// <param name="Half">A box's half lengths; a cylinder's radius in X and half length in Y.</param>
public readonly record struct PadSolid(double3 Centre, double3 AxisX, double3 AxisY, double3 AxisZ,
                                       double3 Half, bool Round);

/// <summary>
/// The top of a structure standing on the ground, such as a launch pad, as the first of its colliders a
/// line dropped from a point meets. The height field knows nothing of a structure, so a wheel over one
/// is otherwise sprung against the ground beneath it.
/// </summary>
public sealed class PadSurface(PadSolid[] solids)
{
    private readonly PadSolid[] _solids = solids;

    public int Count => _solids.Length;

    /// <summary>
    /// How far along a line from a point the structure is first met (m), zero from inside it. False
    /// when nothing is met within the reach.
    /// </summary>
    /// <param name="along">A unit vector.</param>
    public bool TryDrop(double3 from, double3 along, double reach, out double metres)
    {
        metres = reach;
        bool met = false;

        foreach (PadSolid solid in _solids)
        {
            double3 offset = from - solid.Centre;
            double3 o = new(Vec.Dot(offset, solid.AxisX), Vec.Dot(offset, solid.AxisY), Vec.Dot(offset, solid.AxisZ));
            double3 d = new(Vec.Dot(along, solid.AxisX), Vec.Dot(along, solid.AxisY), Vec.Dot(along, solid.AxisZ));

            double enter = 0.0, leave = metres;
            bool inside = solid.Round
                ? Slab(o.Y, d.Y, solid.Half.Y, ref enter, ref leave) && Disc(o, d, solid.Half.X, ref enter, ref leave)
                : Slab(o.X, d.X, solid.Half.X, ref enter, ref leave)
                  && Slab(o.Y, d.Y, solid.Half.Y, ref enter, ref leave)
                  && Slab(o.Z, d.Z, solid.Half.Z, ref enter, ref leave);
            if (!inside) continue;

            metres = enter;
            met = true;
        }

        return met;
    }

    private static bool Slab(double o, double d, double half, ref double enter, ref double leave)
    {
        if (Math.Abs(d) < 1e-12) return Math.Abs(o) <= half;

        double a = (-half - o) / d, b = (half - o) / d;
        enter = Math.Max(enter, Math.Min(a, b));
        leave = Math.Min(leave, Math.Max(a, b));

        return enter <= leave;
    }

    private static bool Disc(double3 o, double3 d, double radius, ref double enter, ref double leave)
    {
        double a = d.X * d.X + d.Z * d.Z;
        double b = o.X * d.X + o.Z * d.Z;
        double c = o.X * o.X + o.Z * o.Z - radius * radius;
        if (a < 1e-12) return c <= 0.0;

        double disc = b * b - a * c;
        if (disc < 0.0) return false;

        double root = Math.Sqrt(disc);
        enter = Math.Max(enter, (-b - root) / a);
        leave = Math.Min(leave, (-b + root) / a);

        return enter <= leave;
    }
}
