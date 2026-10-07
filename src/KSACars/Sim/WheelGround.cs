using Brutal.Numerics;

namespace KSACars;

/// <summary>How far a point in the body's own frame is above the launch pad under it (m); false with none under it.</summary>
public delegate bool PadHeight(double3 atCcf, out double metres);

/// <summary>
/// What is under each of a car's wheels this step: the nearest of the terrain, a launch pad and a
/// road below its hub, as the contacts the drive is stepped against.
///
/// <para>Only the wheels are answered for. A hull that comes down on a road is stopped by the road's
/// own colliders, as the ground's stop it there.</para>
/// </summary>
public static class WheelGround
{
    /// <param name="positionCcf">The centre of mass, from the body's centre, in the body's own frame.</param>
    /// <param name="velocityBody">Its velocity over the ground, in the car's body frame.</param>
    /// <param name="hubs">Each corner's hub at rest, relative to the centre of mass, body frame.</param>
    /// <param name="terrain">The ground's height over <paramref name="meanRadius"/>, asked along directions in the body's own frame.</param>
    /// <param name="roadOver">How far each hub was above the road under it last step, or null; brought up to date.</param>
    /// <param name="hubHeights">Each hub's height over what is under it, straight up, as it is before the contacts are tilted.</param>
    public static void Read(
        double3 positionCcf, doubleQuat body2Ccf, double3 velocityBody, double3 spinBody,
        ReadOnlySpan<double3> hubs, double meanRadius,
        ITerrainHeights terrain, PadHeight? pad, RoadSurface? road,
        Span<double?> roadOver, Span<WheelContact> contacts, Span<double> hubHeights)
    {
        doubleQuat ccf2Body = doubleQuat.Inverse(body2Ccf);

        for (int i = 0; i < hubs.Length; i++)
        {
            double3 hub = hubs[i];

            double3 atCcf = positionCcf + (body2Ccf * hub);
            double radius = Vec.Len(atCcf);
            if (!(radius > 0.0)) continue;
            double3 dirCcf = atCcf / radius;
            if (!terrain.TryHeight(dirCcf, out double height)) continue;

            double hubHeight = radius - (meanRadius + height);
            if (pad is not null && pad(atCcf, out double overPad)) hubHeight = Math.Min(hubHeight, overPad);
            double overRoad = 0.0;
            bool onRoad = road is not null && road.TryHeightOver(atCcf, roadOver[i], out overRoad);
            roadOver[i] = onRoad ? overRoad : null;
            if (onRoad) hubHeight = Math.Min(hubHeight, overRoad);

            hubHeights[i] = hubHeight;
            contacts[i] = new WheelContact(
                Valid: true,
                HubHeight: hubHeight,
                GroundUp: ccf2Body * dirCcf,
                HubVelocity: velocityBody + Vec.Cross(spinBody, hub));
        }

        GroundPlane.Tilt(contacts, hubs);
    }
}
