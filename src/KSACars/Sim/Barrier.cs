using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// A car's side against a deck's barrier, as a push of the mod's own: the wall takes away what speed
/// the car has into it, and rubs it by a share of that push, as steel on a guard rail does.
///
/// <para>The barrier is in the physics' solid too, but KSA has one friction for everything a craft
/// touches, the ground's, and a car that leans on a wall with that is stopped by it. So the car is
/// held <see cref="ClearM"/> short of the solid wall by this, and the solid one is only what is left
/// when this is not running.</para>
/// </summary>
public static class Barrier
{
    /// <summary>The share of the wall's push that rubs the car along it: steel on a steel rail, sliding.</summary>
    public const double Friction = 0.35;

    /// <summary>How much of the speed into the wall comes back off it: a rail gives, and gives little back.</summary>
    public const double Bounce = 0.1;

    /// <summary>How far short of the solid wall a car's side is held, and the share of what it is past that which a step takes back.</summary>
    public const double ClearM = 0.1, Correction = 0.8;

    /// <summary>The fastest a car is pushed back out of a wall it is in, m/s: it is eased out, not thrown.</summary>
    public const double MostPushOutMs = 1.5;

    private const int Passes = 6;

    /// <summary>
    /// The way out through a wall as a car on the road meets it: across its own floor. On a banked deck
    /// the level way out has a share of the car's up, and along that the springs push the car every
    /// step, which a wall would take for the car coming at it. Nothing for a car on its side.
    /// </summary>
    public static double3 Across(double3 outward, double3 up)
    {
        double3 across = outward - (up * Vec.Dot(outward, up));
        return Vec.Dot(across, across) < 0.25 * Vec.Dot(outward, outward) ? Vec.Zero : Vec.Unit(across);
    }

    /// <summary>
    /// The push on a car from the walls its sides are at, as an impulse through its centre of mass and
    /// one about it, all in the car's own frame.
    /// </summary>
    /// <param name="points">Where each of its outermost places is, from its centre of mass.</param>
    /// <param name="outward">At each, the way out of the road through the wall there, level; nothing where there is no wall.</param>
    /// <param name="pastM">At each, how far it is past where the wall holds it: under nothing, and it is clear.</param>
    /// <param name="inverseInertia">The three rows of the inverse of the car's inertia.</param>
    public static DriveImpulse Hold(ReadOnlySpan<double3> points, ReadOnlySpan<double3> outward, ReadOnlySpan<double> pastM,
                                    double3 velocity, double3 spin, double mass, (double3 X, double3 Y, double3 Z) inverseInertia, double dt)
    {
        if (!(mass > 0.0) || !(dt > 0.0)) return default;

        double3 linear = Vec.Zero, angular = Vec.Zero;
        double3 Turned(double3 l) => new(Vec.Dot(inverseInertia.X, l), Vec.Dot(inverseInertia.Y, l), Vec.Dot(inverseInertia.Z, l));

        // Each place in turn and then all again, since holding one corner moves the other.
        for (int pass = 0; pass < Passes; pass++)
        {
            for (int i = 0; i < points.Length && i < outward.Length && i < pastM.Length; i++)
            {
                if (!(pastM[i] > 0.0) || !(Vec.Len(outward[i]) > 0.5)) continue;

                double3 r = points[i], n = Vec.Unit(outward[i]);
                double3 at = velocity + (linear / mass) + Vec.Cross(spin + Turned(angular), r);
                double into = Vec.Dot(at, n), wanted = -Math.Min(Correction * pastM[i] / dt, MostPushOutMs);
                if (into <= wanted) continue;

                // What one unit of push at this place, this way, changes its speed by.
                double3 rn = Vec.Cross(r, n);
                double give = (1.0 / mass) + Vec.Dot(n, Vec.Cross(Turned(rn), r));
                if (!(give > 0.0)) continue;
                double push = ((into * (1.0 + (pass == 0 ? Bounce : 0.0))) - wanted) / give;
                if (!(push > 0.0)) continue;

                // And rubbed along the wall against the way it slides, by no more than stops it there.
                double3 slide = at - (n * into);
                double sliding = Vec.Len(slide);
                double3 rub = Vec.Zero;
                if (sliding > 1e-6)
                {
                    double3 t = slide / sliding, rt = Vec.Cross(r, t);
                    double giveAlong = (1.0 / mass) + Vec.Dot(t, Vec.Cross(Turned(rt), r));
                    rub = t * -Math.Min(Friction * push, giveAlong > 0.0 ? sliding / giveAlong : 0.0);
                }

                double3 impulse = (n * -push) + rub;
                linear += impulse;
                angular += Vec.Cross(r, impulse);
            }
        }
        return new DriveImpulse(linear, angular);
    }
}
