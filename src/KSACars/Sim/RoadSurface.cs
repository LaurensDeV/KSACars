using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// The roads laid on a body as something a wheel can be over: for a point in the body's own frame,
/// how far above the road's surface it is and which way that surface faces.
///
/// <para>Each run of roads is a <see cref="RoadRibbon"/>, and the answer is the ribbon's own surface
/// at the place under the point, not a piece of anything sampled from it: across the asphalt, down
/// the verge and the embankment past its edge, and past an end that is not a deck. A run stops at a
/// junction's mouth, and from there the answer is the junction's plane.</para>
///
/// <para>A road up to <see cref="StepM"/> above a point counts as under it; one higher than that is
/// a bridge overhead, and the highest road that is not is the one answered. Asphalt is answered
/// before a verge or an embankment that is no more than <see cref="AsphaltUnderM"/> above it: the
/// bank of a road up a hillside comes down across the road below it, and a car on that road is on
/// its asphalt.</para>
/// </summary>
public sealed class RoadSurface
{
    /// <summary>
    /// As deep as a deck is. A hub nearer the top than that is inside the deck, where no car can be
    /// driven and one is only on its way back out; a hub further down is below it, under a bridge. A
    /// hull on the road keeps a hub well above its top, so no more is needed for a car that has come
    /// down hard. A road on the ground is a sheet over its fill, shut at the sides by its banks, so
    /// nothing comes to be that far under one.
    /// </summary>
    public const double StepM = RoadRibbon.DeckThickM;

    /// <summary>How far out past the asphalt's edge still counts as on it.</summary>
    public const double EdgeM = 0.02;

    /// <summary>How far under another road's verge or bank a road's asphalt is still what a wheel there is over.</summary>
    public const double AsphaltUnderM = 2.0 * StepM;

    private const double CellM = 8.0;

    private readonly RoadChart? _chart;
    private readonly RoadRibbon[] _ribbons;
    private readonly RoadJunction[] _junctions;
    private readonly Dictionary<(int, int), (int Ribbon, int Place)[]> _cells = [];
    private readonly Dictionary<(int, int), int[]> _junctionCells = [];

    /// <param name="roads">Each road's centre line on its surface, from the body's centre, half its width, and whether it is a ring.</param>
    public RoadSurface(IEnumerable<(double3[] Line, double HalfWidth, bool Closed)> roads)
        : this(roads.Select(r => (r.Line, r.HalfWidth, r.Closed, (double[]?)null)))
    {
    }

    /// <param name="roads">
    /// As above, with how far each point of the line is above the ground, which is taken to be level
    /// across the road; without it the road is on the ground. The lines are the surface as it is,
    /// straight from each point to the next: no ground is smoothed under them and no step in them eased.
    /// </param>
    public RoadSurface(IEnumerable<(double3[] Line, double HalfWidth, bool Closed, double[]? AboveGroundM)> roads)
        : this(Along([.. roads]))
    {
    }

    /// <param name="ribbons">Every run of roads on the body, all on one chart. The junctions are the ones they stop at.</param>
    internal RoadSurface(IReadOnlyList<RoadRibbon> ribbons)
    {
        _ribbons = [.. ribbons];
        _chart = _ribbons.Length > 0 ? _ribbons[0].Chart : null;

        Dictionary<(int, int), List<(int, int)>> cells = [];
        for (int r = 0; r < _ribbons.Length; r++)
        {
            // A place answers for a point as far from it as the surface reaches, and half a step along.
            RoadRibbon ribbon = _ribbons[r];
            double reach = ribbon.ReachM + RoadRibbon.LookupM;
            for (int i = 0; i < ribbon.LookupCount; i++)
            {
                Plan at = ribbon.LookupAt(i);
                (int x0, int y0) = Cell(new Plan(at.E - reach, at.N - reach));
                (int x1, int y1) = Cell(new Plan(at.E + reach, at.N + reach));
                for (int x = x0; x <= x1; x++)
                {
                    for (int y = y0; y <= y1; y++)
                    {
                        if (!cells.TryGetValue((x, y), out List<(int, int)>? places)) cells[(x, y)] = places = [];
                        places.Add((r, i));
                    }
                }
            }
        }
        foreach (((int, int) cell, List<(int, int)> places) in cells) _cells[cell] = [.. places];

        List<RoadJunction> junctions = [];
        foreach (RoadRibbon ribbon in _ribbons)
        {
            if (ribbon.StartJunction is { } start && !junctions.Contains(start)) junctions.Add(start);
            if (ribbon.EndJunction is { } end && !junctions.Contains(end)) junctions.Add(end);
        }
        _junctions = [.. junctions];

        Dictionary<(int, int), List<int>> over = [];
        for (int j = 0; j < _junctions.Length; j++)
        {
            RoadJunction junction = _junctions[j];
            double reach = junction.ReachM + EdgeM;
            (int x0, int y0) = Cell(new Plan(junction.At.E - reach, junction.At.N - reach));
            (int x1, int y1) = Cell(new Plan(junction.At.E + reach, junction.At.N + reach));
            for (int x = x0; x <= x1; x++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    if (!over.TryGetValue((x, y), out List<int>? list)) over[(x, y)] = list = [];
                    list.Add(j);
                }
            }
        }
        foreach (((int, int) cell, List<int> list) in over) _junctionCells[cell] = [.. list];
    }

    private static List<RoadRibbon> Along(List<(double3[] Line, double HalfWidth, bool Closed, double[]? AboveGroundM)> roads)
    {
        List<RoadRibbon> ribbons = [];
        if (!roads.Exists(r => r.Line.Length > 0)) return ribbons;

        // Measured at the height the lines are at, so a metre on the chart is a metre of road.
        double radius = Vec.Len(roads.First(r => r.Line.Length > 0).Line[0]);
        RoadChart chart = RoadChart.About(roads.SelectMany(r => r.Line), radius, new double3(0, 0, 1));
        foreach ((double3[] line, double halfWidth, bool closed, double[]? above) in roads)
        {
            List<Plan> points = [];
            List<double> along = [], height = [], over = [];
            for (int i = 0; i < line.Length; i++)
            {
                Plan at = chart.Of(line[i]);
                double step = points.Count > 0 ? (at - points[^1]).Len : 0.0;
                if (points.Count > 0 && !(step >= 1e-6)) continue;
                along.Add(points.Count > 0 ? along[^1] + step : 0.0);
                points.Add(at);
                height.Add(Vec.Len(line[i]) - radius);
                over.Add(above?[i] ?? 0.0);
            }
            if (points.Count < 2) continue;

            RoadLine centre = RoadLine.Through(points, closed);
            double period = closed ? centre.LengthM : 0.0;
            RoadProfile profile = new([0.0, centre.LengthM], [0.0, 0.0], [0.0, 0.0], [2.0 * halfWidth, 2.0 * halfWidth], closed, 0.0,
                                      ground: RoadGround.Along(MonotoneCurve.Straight(along, height, period)));
            ribbons.Add(RoadRibbon.Over(chart, centre, profile, MonotoneCurve.Straight(along, over, period).At));
        }
        return ribbons;
    }

    private static (int, int) Cell(Plan p) => ((int)Math.Floor(p.E / CellM), (int)Math.Floor(p.N / CellM));

    /// <summary>How far a point is above the road under it (m), negative when it is in it; false with no road under it.</summary>
    public bool TryHeightOver(double3 at, out double metres) => TryHeightOver(at, null, out metres);

    /// <summary>
    /// The same for a point that was <paramref name="last"/> above a road a moment ago. One that was
    /// in a road is still over that road however far into it it has got: the roads it may be on are
    /// those no more than <see cref="StepM"/> above the surface it was on, which is what tells a car
    /// that has run into a ramp from one under a bridge.
    /// </summary>
    public bool TryHeightOver(double3 at, double? last, out double metres) => TryLocate(at, last, out metres, out _);

    /// <summary>
    /// The same, with how far out past the asphalt the point is (m): nothing on the road itself, and
    /// on the verge or the embankment how far from the road's edge or its end.
    /// </summary>
    public bool TryLocate(double3 at, double? last, out double metres, out double outM) => TryLocate(at, last, out metres, out outM, out _);

    /// <summary>
    /// The same, with the way the surface faces there, where it is the asphalt of a road that was
    /// laid. A verge, an embankment and a surface made from a line of points have creases and steps
    /// in them, and what a wheel a third of a metre across meets on one is not the way one point of
    /// it faces: those answer none, as the terrain does.
    /// </summary>
    public bool TryLocate(double3 at, double? last, out double metres, out double outM, out double3? facing)
    {
        metres = double.PositiveInfinity;
        outM = 0.0;
        facing = null;
        double radius = Vec.Len(at);
        if (_chart is null || !(radius > 0.0)) return false;

        Plan place = _chart.Of(at);
        _cells.TryGetValue(Cell(place), out (int Ribbon, int Place)[]? places);
        _junctionCells.TryGetValue(Cell(place), out int[]? junctions);
        if (places is null && junctions is null) return false;

        // A wheel above its road is asked as one new to it, or a deck it flew in over would not be under it.
        double deepest = Math.Min(last ?? 0.0, 0.0) - StepM;
        double height = radius - _chart.RadiusM, overEarth = double.PositiveInfinity, outEarth = 0.0;
        (RoadRibbon? Ribbon, RoadRibbon.Section At, double D, double Height, double Along, double Across) best = default;

        RoadJunction? on = null;
        foreach (int j in junctions ?? [])
        {
            RoadJunction junction = _junctions[j];
            if (!junction.Surface(place, EdgeM, out double surface, out double out_)) continue;

            double over = height - surface;
            if (over < deepest) continue;
            if (out_ > EdgeM)
            {
                if (over < overEarth) (overEarth, outEarth) = (over, out_);
            }
            else if (over < metres)
            {
                (metres, on) = (over, junction);
            }
        }

        foreach ((int r, int i) in places ?? [])
        {
            // Only from the place nearest the point of those either side of it: once a ribbon each time it passes.
            RoadRibbon ribbon = _ribbons[r];
            double here = Off(ribbon, i, place);
            if (Off(ribbon, i - 1, place) < here || Off(ribbon, i + 1, place) <= here) continue;

            if (!ribbon.Locate(place, i, out RoadRibbon.Section section, out double d, out double beyond)) continue;
            if (!ribbon.Surface(section, d, beyond, out double surface, out double out_, out double along, out double across)) continue;

            double over = height - surface;
            if (over < deepest) continue;
            if (out_ > EdgeM)
            {
                if (over < overEarth) (overEarth, outEarth) = (over, out_);
            }
            else if (over < metres)
            {
                metres = over;
                on = null;
                best = (ribbon, section, d, surface, along, across);
            }
        }

        if ((best.Ribbon is null && on is null) || metres - overEarth > AsphaltUnderM)
        {
            (metres, outM) = (overEarth, outEarth);
            return !double.IsPositiveInfinity(metres);
        }
        if (on is not null) facing = on.Normal(place);
        else if (best.Ribbon!.Laid) facing = best.Ribbon.Normal(best.At, best.D, best.Height, best.Along, best.Across);
        return true;
    }

    /// <summary>What of the roads is over a place on the ground.</summary>
    public enum Cover
    {
        /// <summary>No road, nor a road's verge or bank.</summary>
        None,

        /// <summary>A deck and nothing else: the ground under a bridge, which is still ground.</summary>
        Deck,

        /// <summary>A road that stands on the ground, or its verge or its bank: what is there is under it.</summary>
        Ground,
    }

    /// <summary>
    /// What is over the ground in a direction from the body's centre, whatever the height of anything:
    /// the roads' own outline, as wide as each is there and the shape each junction is, which is what
    /// says whether a thing standing on the ground is under a road.
    /// </summary>
    public Cover Over(double3 dir)
    {
        if (_chart is null) return Cover.None;

        Plan place = _chart.Of(dir);
        _cells.TryGetValue(Cell(place), out (int Ribbon, int Place)[]? places);
        _junctionCells.TryGetValue(Cell(place), out int[]? junctions);

        Cover cover = Cover.None;
        foreach (int j in junctions ?? [])
        {
            RoadJunction junction = _junctions[j];
            if (!junction.Surface(place, EdgeM, out _, out _)) continue;
            if (!junction.Deck) return Cover.Ground;
            cover = Cover.Deck;
        }
        foreach ((int r, int i) in places ?? [])
        {
            RoadRibbon ribbon = _ribbons[r];
            double here = Off(ribbon, i, place);
            if (Off(ribbon, i - 1, place) < here || Off(ribbon, i + 1, place) <= here) continue;
            if (!ribbon.Locate(place, i, out RoadRibbon.Section section, out double d, out double beyond)) continue;
            if (!ribbon.Surface(section, d, beyond, out _, out _, out _, out _)) continue;
            if (!ribbon.DeckOver(Math.Min((int)(section.S / ribbon.SideStepM), ribbon.SideStretches - 1))) return Cover.Ground;
            cover = Cover.Deck;
        }
        return cover;
    }

    /// <summary>
    /// Whether a place is at a deck's barrier: within the deck's length and level with its edge on that
    /// side, whichever side of the edge it is on.
    /// </summary>
    /// <param name="at">The place, from the body's centre in its own frame.</param>
    /// <param name="pastM">How far past the asphalt's edge the place is: under nothing, and it is short of the wall.</param>
    /// <param name="outward">The level way out of the road through the wall there.</param>
    public bool TryBarrier(double3 at, out double pastM, out double3 outward)
    {
        pastM = double.NegativeInfinity;
        outward = default;
        double radius = Vec.Len(at);
        if (_chart is null || !(radius > 0.0)) return false;

        Plan place = _chart.Of(at);
        if (!_cells.TryGetValue(Cell(place), out (int Ribbon, int Place)[]? places)) return false;

        double height = radius - _chart.RadiusM, nearest = double.PositiveInfinity;
        foreach ((int r, int i) in places)
        {
            RoadRibbon ribbon = _ribbons[r];
            double here = Off(ribbon, i, place);
            if (Off(ribbon, i - 1, place) < here || Off(ribbon, i + 1, place) <= here) continue;
            if (!ribbon.Locate(place, i, out RoadRibbon.Section section, out double d, out double beyond) || !section.Deck || beyond > 0.0) continue;

            // Level with the deck's edge on that side, give or take what a car's side stands over it and a barrier's height.
            double side = d < 0.0 ? -1.0 : 1.0;
            double over = height - (section.Height + (side * section.HalfWidth * section.BankTan));
            if (over < -BarrierUnderM || over > BarrierOverM || Math.Abs(over) >= nearest) continue;

            nearest = Math.Abs(over);
            pastM = Math.Abs(d) - section.HalfWidth;
            ribbon.Chart.Compass(section.At, out _, out double3 east, out double3 north);
            outward = Vec.Unit((east * section.Left.E) + (north * section.Left.N)) * side;
        }
        return !double.IsInfinity(nearest);
    }

    // How far under a deck's edge and over it a car's side is still at its barrier.
    private const double BarrierUnderM = 1.0, BarrierOverM = 2.0;

    // How far a point is from one of a ribbon's lookup places, squared; no distance at all past the end of an open one.
    private static double Off(RoadRibbon ribbon, int index, Plan place)
    {
        int count = ribbon.LookupCount;
        if (index < 0 || index >= count)
        {
            if (!ribbon.Closed) return double.PositiveInfinity;
            index = ((index % count) + count) % count;
        }
        Plan off = place - ribbon.LookupAt(index);
        return Plan.Dot(off, off);
    }
}
