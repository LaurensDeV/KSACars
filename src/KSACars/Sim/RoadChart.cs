using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// A flat chart of the ground round one place on a body, so a circuit's widths, offsets and lookups
/// are plain geometry in metres east and north.
///
/// <para>Stereographic, which keeps every angle: a road square to another on the chart is square to
/// it on the ground. A length <c>r</c> metres from the chart's centre is drawn too long by
/// <c>(r / 2R)^2</c> of itself, 2 parts in a million at 5 km on Luna, and that is all the error
/// there is.</para>
/// </summary>
internal sealed class RoadChart
{
    public double3 Centre { get; }
    public double3 East { get; }
    public double3 North { get; }
    public double RadiusM { get; }

    /// <param name="centre">The direction from the body's centre the chart is flat at.</param>
    /// <param name="pole">The body's north pole, as a direction from its centre.</param>
    public RoadChart(double3 centre, double radiusM, double3 pole)
    {
        Centre = Vec.Unit(centre);
        RadiusM = radiusM;
        double3 north = Vec.RejectFrom(pole, Centre);
        North = Vec.Len(north) > 1e-6 ? Vec.Unit(north) : Vec.AnyPerpendicular(Centre);
        East = Vec.Cross(North, Centre);
    }

    /// <summary>A chart flat at the middle of <paramref name="places"/>, each a direction from the body's centre.</summary>
    public static RoadChart About(IEnumerable<double3> places, double radiusM, double3 pole)
    {
        double3 sum = Vec.Zero;
        foreach (double3 place in places) sum += Vec.Unit(place);
        return new RoadChart(Vec.Len(sum) > 1e-9 ? sum : new double3(1, 0, 0), radiusM, pole);
    }

    /// <summary>Where a direction from the body's centre is on the chart. Its length is not used.</summary>
    public Plan Of(double3 dir)
    {
        double3 d = Vec.Unit(dir);
        double k = 2.0 * RadiusM / (1.0 + Vec.Dot(d, Centre));
        return new Plan(k * Vec.Dot(d, East), k * Vec.Dot(d, North));
    }

    /// <summary>The direction from the body's centre a place on the chart is in.</summary>
    public double3 Dir(Plan at)
    {
        double u = at.E / RadiusM, v = at.N / RadiusM, q = 0.25 * ((u * u) + (v * v));
        return ((East * u) + (North * v) + (Centre * (1.0 - q))) / (1.0 + q);
    }

    /// <summary>How many metres of chart a metre on the body's mean sphere is there: 1 + (r / 2R)^2.</summary>
    public double Scale(Plan at)
    {
        double u = at.E / RadiusM, v = at.N / RadiusM;
        return 1.0 + (0.25 * ((u * u) + (v * v)));
    }

    /// <summary>The direction a place is in, and the chart's east and north as level unit vectors there.</summary>
    public void Compass(Plan at, out double3 dir, out double3 east, out double3 north)
    {
        dir = Dir(at);
        double3 lean = Centre + dir;
        east = Vec.Unit(East - (lean * (0.5 * at.E / RadiusM)));
        north = Vec.Unit(North - (lean * (0.5 * at.N / RadiusM)));
    }
}
