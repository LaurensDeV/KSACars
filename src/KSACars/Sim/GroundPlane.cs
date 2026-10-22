using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// The ground under a car as one plane, through what is under its four wheels.
///
/// <para>The drive damps each wheel by how fast its hub closes on the ground along the ground's up.
/// With straight up for that, a car running down a slope is closing on the ground at the slope's
/// share of its speed as far as its dampers know, and they throw it off the road. Along the plane's
/// own up, a car following a slope closes on nothing.</para>
/// </summary>
public static class GroundPlane
{
    // Past this lean from straight up the four points are a wall or a wheel hung over an edge, not a road.
    private const double LeastCos = 0.5;

    /// <summary>
    /// Gives every wheel the plane's up for its ground's up, and its height along that. Left as they
    /// were unless there are four wheels, front pair first, each with ground under it.
    /// </summary>
    public static void Tilt(Span<WheelContact> contacts, ReadOnlySpan<double3> hubs)
    {
        if (contacts.Length != 4 || hubs.Length != 4) return;

        Span<double3> under = stackalloc double3[4];
        double3 straightUp = Vec.Zero;
        for (int i = 0; i < 4; i++)
        {
            if (!contacts[i].Valid || !double.IsFinite(contacts[i].HubHeight)) return;
            under[i] = hubs[i] - (contacts[i].GroundUp * contacts[i].HubHeight);
            straightUp += contacts[i].GroundUp;
        }

        double3 normal = Vec.Cross(under[3] - under[0], under[2] - under[1]);
        if (!(Vec.Len(normal) > 1e-9) || !(Vec.Len(straightUp) > 1e-9)) return;
        normal = Vec.Unit(normal);
        straightUp = Vec.Unit(straightUp);
        if (Vec.Dot(normal, straightUp) < 0.0) normal = -normal;
        if (Vec.Dot(normal, straightUp) < LeastCos) return;

        for (int i = 0; i < 4; i++)
        {
            contacts[i] = contacts[i] with
            {
                GroundUp = normal,
                HubHeight = contacts[i].HubHeight * Vec.Dot(normal, contacts[i].GroundUp),
            };
        }
    }
}
