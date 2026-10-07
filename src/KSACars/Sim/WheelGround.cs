using Brutal.Numerics;

namespace KSACars;

/// <summary>How far a point in the body's own frame is above the launch pad under it (m); false with none under it.</summary>
public delegate bool PadHeight(double3 atCcf, out double metres);

/// <summary>
/// What sets a car that has run into a road back on it: a lift along <paramref name="Up"/>, straight
/// up in the car's body frame, and that much speed along it given back.
/// </summary>
public readonly record struct RoadStop(double3 Up, double LiftM, double SpeedMs)
{
    public bool Fired => LiftM > 0.0;
}

/// <summary>
/// What is under each of a car's wheels this step: the nearest of the terrain, a launch pad and a
/// road below its hub, as the contacts the drive is stepped against.
///
/// <para>Nothing of KSA's stands where a road is, so a car that has run into one is not stopped as
/// the ground stops it, and the springs, which push only so hard, would let it through. Past
/// <see cref="RoadBumpStopM"/> beyond a wheel's travel the car is to be set back on the surface and
/// lose what speed it had downwards: the contacts answered are already those of the car moved so,
/// and the caller owes the craft the same move.</para>
/// </summary>
public static class WheelGround
{
    /// <summary>
    /// How far past the end of its travel a wheel may go into a road before the car is set back on it:
    /// that much is the bump stop's to push out, as it does on the ground.
    /// </summary>
    public const double RoadBumpStopM = 0.10;

    /// <param name="positionCcf">The centre of mass, from the body's centre, in the body's own frame.</param>
    /// <param name="velocityBody">Its velocity over the ground, in the car's body frame.</param>
    /// <param name="hubs">Each corner's hub at rest, relative to the centre of mass, body frame.</param>
    /// <param name="terrain">The ground's height over <paramref name="meanRadius"/>, asked along directions in the body's own frame.</param>
    /// <param name="roadOver">How far each hub was above the road under it last step, or null; brought up to date.</param>
    /// <param name="hubHeights">Each hub's height over what is under it, before the car is set back on a road.</param>
    public static RoadStop Read(
        double3 positionCcf, doubleQuat body2Ccf, double3 velocityBody, double3 spinBody,
        ReadOnlySpan<double3> hubs, BuggyProfile profile, double meanRadius,
        ITerrainHeights terrain, PadHeight? pad, RoadSurface? road,
        Span<double?> roadOver, Span<WheelContact> contacts, Span<double> hubHeights)
    {
        doubleQuat ccf2Body = doubleQuat.Inverse(body2Ccf);
        BuggyCorner[] corners = profile.Corners;

        // How far the deepest wheel is into a road past the end of its travel and its bump stop's.
        double sunk = 0.0;
        for (int i = 0; i < corners.Length; i++)
        {
            double3 hub = hubs[i];

            double3 atCcf = positionCcf + Turn(hub, body2Ccf);
            double radius = Vec.Len(atCcf);
            if (!(radius > 0.0)) continue;
            double3 dirCcf = atCcf / radius;
            if (!terrain.TryHeight(dirCcf, out double height)) continue;

            double hubHeight = radius - (meanRadius + height);
            if (pad is not null && pad(atCcf, out double overPad)) hubHeight = Math.Min(hubHeight, overPad);
            double overRoad = 0.0;
            bool onRoad = road is not null && road.TryHeightOver(atCcf, roadOver[i], out overRoad);
            roadOver[i] = onRoad ? overRoad : null;
            if (onRoad && overRoad < hubHeight)
            {
                hubHeight = overRoad;
                sunk = Math.Max(sunk, corners[i].Radius - profile.BumpTravel - RoadBumpStopM - overRoad);
            }

            hubHeights[i] = hubHeight;
            contacts[i] = new WheelContact(
                Valid: true,
                HubHeight: hubHeight,
                GroundUp: Turn(dirCcf, ccf2Body),
                HubVelocity: velocityBody + Vec.Cross(spinBody, hub));
        }

        RoadStop stop = default;
        if (sunk > 0.0)
        {
            double3 straightUp = Turn(Vec.Unit(positionCcf), ccf2Body);
            double falling = Math.Max(-Vec.Dot(velocityBody, straightUp), 0.0);
            stop = new RoadStop(straightUp, sunk, falling);
            for (int i = 0; i < contacts.Length; i++)
            {
                if (!contacts[i].Valid) continue;
                contacts[i] = contacts[i] with
                {
                    HubHeight = contacts[i].HubHeight + sunk,
                    HubVelocity = contacts[i].HubVelocity + (straightUp * falling),
                };
                if (roadOver[i] is { } over) roadOver[i] = over + sunk;
            }
        }

        GroundPlane.Tilt(contacts, hubs);
        return stop;
    }

    // A vector turned by a rotation, in the arithmetic KSA's own double3.Transform uses and not that of
    // Brutal's operator, which differs in the last bit: the craft is moved by what this answers.
    private static double3 Turn(double3 v, doubleQuat q)
    {
        double x2 = q.X + q.X, y2 = q.Y + q.Y, z2 = q.Z + q.Z;
        double wx = q.W * x2, wy = q.W * y2, wz = q.W * z2;
        double xx = q.X * x2, xy = q.X * y2, xz = q.X * z2;
        double yy = q.Y * y2, yz = q.Y * z2, zz = q.Z * z2;
        return new double3(
            (v.X * (1.0 - yy - zz)) + (v.Y * (xy - wz)) + (v.Z * (xz + wy)),
            (v.X * (xy + wz)) + (v.Y * (1.0 - xx - zz)) + (v.Z * (yz - wx)),
            (v.X * (xz - wy)) + (v.Y * (yz + wx)) + (v.Z * (1.0 - xx - yy)));
    }
}
