using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// What is wrong with a circuit as it is laid, for whoever is drawing it: nothing here stops a road
/// being drawn, and each of these is a road that will not drive as it looks.
/// </summary>
/// <param name="Text">What is wrong, in a line.</param>
/// <param name="At">Where, as a direction from the body's centre.</param>
/// <param name="Node">The circuit's point it is at, where it is at one.</param>
internal sealed record RoadWarning(string Text, double3 At, int? Node = null)
{
    /// <summary>A grade past which a car of its kind stops, as they were driven: the Eldorado from rest, the buggy with a run at it, and any car.</summary>
    public const double HeavyCarGrade = 0.45, LightCarGrade = 0.60, AnyCarGrade = 0.85;

    public static List<RoadWarning> Of(Circuit circuit, RoadLaying.Network network, Func<double, double, double3> dirOf)
    {
        List<RoadWarning> warnings = [];
        double3 DirOf(int node) => circuit.Find(node) is { } n ? dirOf(n.LatDeg, n.LonDeg) : default;

        foreach ((int node, string why) in network.Refused)
        {
            warnings.Add(new RoadWarning($"No junction at point {node}: {why}. Its roads lie over one another.", DirOf(node), node));
        }

        foreach (RoadJunction junction in network.Junctions)
        {
            foreach (RoadJunction.Arm arm in junction.Arms)
            {
                // Its mouth is where its corners end, or as far out as a road of its length lets it be: at that, it was cut short.
                if (arm.MouthS < arm.CapS - 1e-6) continue;
                warnings.Add(new RoadWarning($"The road from point {arm.Far} is too short for the junction at {junction.Node}: "
                                             + "its corners are cut off where they should round. Move the point away.", DirOf(arm.Far), arm.Far));
            }
        }

        foreach (RoadRibbon ribbon in network.Ribbons)
        {
            foreach (RoadLine.Tight tight in ribbon.TooTight())
            {
                RoadRibbon.Section at = ribbon.At(0.5 * (tight.FromS + tight.ToS));
                warnings.Add(new RoadWarning($"A bend of {tight.LeastRadiusM:F0} m radius, tighter than the {tight.NeededM:F0} m a road this wide can turn in: "
                                             + "its inside edge folds over itself. Narrow the road or open the bend.", Vec.Unit(ribbon.Point(at, 0.0, at.Height))));
            }

            double steepest = 0.0, where = 0.0;
            for (double s = 0.0; s <= ribbon.LengthM; s += 2.0)
            {
                double slope = Math.Abs(ribbon.At(s).Slope);
                if (slope > steepest) (steepest, where) = (slope, s);
            }
            if (steepest > HeavyCarGrade)
            {
                string who = steepest > AnyCarGrade ? "no car climbs it"
                    : steepest > LightCarGrade ? "only the F2004 climbs it"
                    : "the Eldorado stops on it";
                RoadRibbon.Section at = ribbon.At(where);
                warnings.Add(new RoadWarning($"A grade of {steepest * 100.0:F0}%: {who}.", Vec.Unit(ribbon.Point(at, 0.0, at.Height))));
            }
        }
        return warnings;
    }
}
