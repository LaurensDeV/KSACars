using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// A line to drive along, and how far along it a car is: the centre lines of a circuit's roads, taken
/// in the order the route passes through its points, as samples a metre apart.
///
/// <para>Progress is searched for a short way ahead of where it last was and nowhere else. The nearest
/// point of any road is the wrong one at a junction, at a crossing and where a road passes over
/// another, and a route that comes back over itself is only told apart by how far along it the car
/// has got.</para>
///
/// <para>Across a junction the line is a curve from the middle of the mouth come in by to the middle of
/// the one left by, leaving each along its road.</para>
/// </summary>
internal sealed class Route
{
    /// <param name="At">On the road's surface, from the body's centre.</param>
    /// <param name="S">How far along the route, m.</param>
    /// <param name="Tangent">Level, along the route.</param>
    /// <param name="Across">Level, to the left of it.</param>
    /// <param name="Curvature">Seen from above, 1/m, a left turn positive.</param>
    /// <param name="Slope">Climb for each metre along.</param>
    /// <param name="Bank">The tangent of the road's lean across, the left of the route up positive.</param>
    /// <param name="Vertical">How the climb itself bends, 1/m: positive through a dip, negative over a crest.</param>
    /// <param name="CentreM">How far to the left of this the road's centre is, m.</param>
    public readonly record struct Sample(double3 At, double S, double3 Tangent, double3 Across, double Curvature,
                                         double Slope, double Vertical, double HalfWidth, double CentreM, double Bank = 0.0);

    public const double SpacingM = 1.0;

    /// <summary>What a turn is measured over, so a kink between two straight stretches is a bend and not an instant.</summary>
    public const double SmoothOverM = 3.0;

    /// <summary>A turn of more than this at one point of a road is a kink: a point with no corner to it, or a junction that could not be made.</summary>
    public const double KinkDeg = 30.0;

    /// <summary>The share of the road's half width a kink is cut inside by, and the most of the road either side that is given up to it, in half widths.</summary>
    public const double KinkCut = 0.8, KinkReach = 2.0;

    // A line moved to the outside of a kink gets there over this many times as far as it is moved, and no less than this.
    private const double SwingOver = 8.0, LeastSwingM = 10.0;

    // What the climb's bend is measured over, either side: the line is straight between points two metres apart.
    private const double VerticalOverM = 4.0;

    private readonly Sample[] _samples;

    public ReadOnlySpan<Sample> Samples => _samples;
    public int Count => _samples.Length;
    public double LengthM { get; }

    /// <summary>Whether the route ends where it starts, and is driven round and round.</summary>
    public bool Closed { get; }

    private Route(Sample[] samples, double lengthM, bool closed)
    {
        _samples = samples;
        LengthM = lengthM;
        Closed = closed;
    }

    /// <summary>
    /// The route through <paramref name="through"/>, a circuit's points in order, ending on the first
    /// for a lap; or with none given, from the circuit's first road on along whatever road goes through
    /// each point, until it is back where it started or the road ends. Null with the reason.
    /// </summary>
    /// <param name="groundAt">The ground's height over the body's mean radius, in a direction from its centre.</param>
    /// <param name="offsetM">How far to the left of the roads' centre line the route runs.</param>
    /// <param name="turnRadiusM">The tightest turn the car that drives it makes, or nothing for any car: see <see cref="Along"/>.</param>
    /// <param name="raceInsideM">More than nothing, and the route is the racing line that keeps this far inside each edge: see <see cref="RacingLine"/>.</param>
    public static Route? Of(Circuit circuit, Func<double, double, double3> dirOf, double radiusM, Func<double3, double> groundAt,
                            double liftM, double spacingM, IReadOnlyList<int>? through, double offsetM, out string why,
                            double turnRadiusM = 0.0, double raceInsideM = 0.0, IReadOnlyList<double>? nudgesM = null)
    {
        why = "";
        List<int> path = through is { Count: > 0 } ? [.. through] : Following(circuit, dirOf, radiusM);
        if (path.Count < 2)
        {
            why = "a route needs two points joined by a road";
            return null;
        }

        RoadLaying.Network laid = RoadLaying.Laid(circuit, dirOf, radiusM, groundAt, liftM, spacingM);
        List<RoadRibbon> ribbons = laid.Ribbons;
        List<double3> line = [];
        List<double> halfWidths = [], banks = [];

        // From the road just driven across the junction at `at` to the road on to `next`.
        void Cross(int from, int at, int next)
        {
            if (laid.Junctions.FirstOrDefault(j => j.Node == at) is not { } junction) return;
            if (junction.ArmTo(from) is not { } into || junction.ArmTo(next) is not { } outOf || ReferenceEquals(into, outOf)) return;
            List<double3> across = junction.Across(into, outOf, spacingM);
            for (int i = 1; i < across.Count; i++)
            {
                line.Add(across[i]);
                halfWidths.Add(into.HalfWidth + ((outOf.HalfWidth - into.HalfWidth) * i / (across.Count - 1)));
                banks.Add(0.0);
            }
        }

        for (int k = 1; k < path.Count; k++)
        {
            int a = path[k - 1], b = path[k];
            if (k > 1) Cross(path[k - 2], a, b);
            RoadRibbon? on = null;
            RoadRibbon.Span span = default;
            foreach (RoadRibbon ribbon in ribbons)
            {
                foreach (RoadRibbon.Span s in ribbon.Spans)
                {
                    if ((s.From == a && s.To == b) || (s.From == b && s.To == a)) (on, span) = (ribbon, s);
                }
            }
            if (on is null)
            {
                why = $"no road joins {a} and {b}";
                return null;
            }

            double from = span.From == a ? span.FromS : span.ToS, to = span.From == a ? span.ToS : span.FromS;
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(to - from) / Math.Max(spacingM, 0.01)));
            for (int i = line.Count == 0 ? 0 : 1; i <= steps; i++)
            {
                // Just short of each end, so a closed run's last point is not its first come round again.
                double s = Math.Clamp(from + ((to - from) * i / steps), Math.Min(from, to) + 1e-9, Math.Max(from, to) - 1e-9);
                RoadRibbon.Section section = on.At(s);
                line.Add(on.Point(section, 0.0, section.Height));
                halfWidths.Add(section.HalfWidth);
                banks.Add(to >= from ? section.BankTan : -section.BankTan);
            }
        }

        bool closed = path.Count > 2 && path[0] == path[^1];
        if (closed)
        {
            Cross(path[^2], path[0], path[1]);
            line.RemoveAt(line.Count - 1);
            halfWidths.RemoveAt(halfWidths.Count - 1);
            banks.RemoveAt(banks.Count - 1);
        }
        if (Along([.. line], [.. halfWidths], closed, offsetM, turnRadiusM, [.. banks], raceInsideM, nudgesM) is not { } route)
        {
            why = "the route has no length";
            return null;
        }
        return route;
    }

    // From the first road on, along the road that goes through each point.
    private static List<int> Following(Circuit circuit, Func<double, double, double3> dirOf, double radiusM)
    {
        List<int> path = [];
        if (circuit.Roads.Count == 0) return path;

        Dictionary<int, double3> at = RoadLayout.Places(circuit, dirOf, radiusM);

        Circuit.Road first = circuit.Roads[0];
        path.Add(first.From);
        path.Add(first.To);
        HashSet<(int, int)> driven = [(first.From, first.To)];
        while (RoadLayout.Through(circuit, at, path[^1], path[^2]) is { } onward && driven.Add((path[^1], onward)))
        {
            path.Add(onward);
        }
        return path;
    }

    /// <summary>
    /// The route along a line of points on the road's surface, from the body's centre; a closed one
    /// carries on from its last point to its first. Null where the line has no length.
    ///
    /// <para>A kink cut inside as far as the asphalt allows is a turn of 4 m radius where two 10 m roads
    /// meet at 120 degrees, which a car that turns in 8 m leaves the road on. So where
    /// <paramref name="turnRadiusM"/> is more than that the line swings to the outside of the road
    /// before the kink and back after it, which is the only other room there is.</para>
    /// </summary>
    public static Route? Along(double3[] line, double[] halfWidths, bool closed, double offsetM, double turnRadiusM = 0.0,
                               double[]? banks = null, double raceInsideM = 0.0, IReadOnlyList<double>? nudgesM = null)
    {
        int pieces = closed ? line.Length : line.Length - 1;
        if (pieces < 1 || halfWidths.Length != line.Length) return null;

        double[] start = new double[pieces + 1];
        for (int i = 0; i < pieces; i++) start[i + 1] = start[i] + Vec.Len(line[(i + 1) % line.Length] - line[i]);
        double length = start[pieces];
        if (!(length > SpacingM)) return null;

        int steps = Math.Max(2, (int)Math.Round(length / SpacingM));
        int count = closed ? steps : steps + 1;
        double3[] at = new double3[count];
        double[] half = new double[count];
        double[] bank = new double[count];
        for (int i = 0, piece = 0; i < count; i++)
        {
            double s = length * i / steps;
            while (piece < pieces - 1 && start[piece + 1] < s) piece++;
            double span = start[piece + 1] - start[piece];
            double t = span > 0.0 ? Math.Clamp((s - start[piece]) / span, 0.0, 1.0) : 0.0;
            int next = (piece + 1) % line.Length;
            at[i] = line[piece] + ((line[next] - line[piece]) * t);
            half[i] = halfWidths[piece] + ((halfWidths[next] - halfWidths[piece]) * t);
            if (banks is not null && banks.Length == line.Length) bank[i] = banks[piece] + ((banks[next] - banks[piece]) * t);
        }

        // No car turns on the spot, so a kink is rounded: between a point either side of it the line is
        // a curve leaving each along the road, cut inside the kink and still on the asphalt.
        double each = length / steps;
        int Wrapped(int i) => ((i % count) + count) % count;
        List<(int Middle, int Span, double Turn, double OutM)> kinks = [];
        double[] swing = new double[count];
        for (int k = closed ? 0 : 1; k < line.Length - (closed ? 0 : 1); k++)
        {
            double3 before = line[(k + line.Length - 1) % line.Length], after = line[(k + 1) % line.Length];
            double3 into = Vec.RejectFrom(line[k] - before, line[k]), outOf = Vec.RejectFrom(after - line[k], line[k]);
            double turn = Vec.AngleBetween(into, outOf);
            // A turn of just that is a kink whichever way its last digit falls.
            if (turn <= (KinkDeg * Math.PI / 180.0) - 1e-6) continue;

            // An arc touching both roads a distance out from their middles passes inside the kink by
            // radius x (secant - 1) - out x secant, and that is all the cut there is room for.
            double halfTurn = Math.Tan(0.5 * turn), secant = 1.0 / Math.Cos(0.5 * turn);
            double room = KinkCut * halfWidths[k];
            double reachM = Math.Min(room * halfTurn / (secant - 1.0), KinkReach * halfWidths[k]);
            double outM = 0.0;
            if (turnRadiusM > reachM / halfTurn)
            {
                outM = Math.Clamp(((turnRadiusM * (secant - 1.0)) - room) / secant, 0.0, room);
                reachM = (((room + (outM * secant)) / (secant - 1.0)) - outM) * halfTurn;
            }

            int middle = (int)Math.Round(start[k] / length * steps);
            int span = (int)Math.Round(reachM / each);
            span = closed ? Math.Min(span, count / 4) : Math.Min(span, Math.Min(middle - 1, count - 2 - middle));
            if (span < 2) continue;
            kinks.Add((middle, span, turn, outM));
            if (!(outM > 0.0)) continue;

            int over = (int)Math.Round(Math.Max(SwingOver * outM, LeastSwingM) / each);
            over = Math.Max(closed ? Math.Min(over, count / 4) : Math.Min(over, Math.Min(middle - span - 1, count - 2 - middle - span)), 0);
            double toLeft = Vec.Dot(Vec.Cross(into, outOf), line[k]) > 0.0 ? -outM : outM;
            for (int j = -span - over; j <= span + over; j++)
            {
                double t = over > 0 ? Math.Clamp((span + over - Math.Abs(j)) / (double)over, 0.0, 1.0) : 1.0;
                swing[Wrapped(middle + j)] += toLeft * t * t * (3.0 - (2.0 * t));
            }
        }

        if (kinks.Exists(k => k.OutM > 0.0))
        {
            double3[] swung = new double3[count];
            for (int i = 0; i < count; i++) swung[i] = at[i] + (Vec.Cross(Vec.Unit(at[i]), Heading(at, i, 1, closed)) * swing[i]);
            at = swung;
        }
        foreach ((int middle, int span, double turn, double outM) in kinks)
        {
            double3 a = at[Wrapped(middle - span)], b = at[Wrapped(middle + span)];
            double3 leave = Vec.Unit(a - at[Wrapped(middle - span - 1)]), arrive = Vec.Unit(at[Wrapped(middle + span + 1)] - b);
            double handle = 4.0 / 3.0 * Math.Tan(0.25 * turn) * ((span * each / Math.Tan(0.5 * turn)) + outM);
            for (int j = 1; j < 2 * span; j++)
            {
                at[Wrapped(middle - span + j)] = RoadCurve.Bezier(a, a + (leave * handle), b - (arrive * handle), b, j / (2.0 * span));
            }
        }

        double[] left = new double[count];
        Array.Fill(left, offsetM);
        if (raceInsideM > 0.0)
        {
            left = RacingLine(at, half, closed, raceInsideM);

            // Moved to the left by as much as is asked at each of as many places evenly along it, and between
            // them in proportion: a line learnt, where the one that turns least is not the quickest. Still on the road.
            if (nudgesM is { Count: > 0 })
            {
                for (int i = 0; i < count; i++)
                {
                    double place = (double)i / count * nudgesM.Count;
                    int k = (int)place % nudgesM.Count, next = closed ? (k + 1) % nudgesM.Count : Math.Min(k + 1, nudgesM.Count - 1);
                    double most = Math.Max(half[i] - raceInsideM, 0.0);
                    left[i] = Math.Clamp(left[i] + nudgesM[k] + ((nudgesM[next] - nudgesM[k]) * (place - Math.Floor(place))), -most, most);
                }
            }
        }
        if (offsetM != 0.0 || raceInsideM > 0.0)
        {
            double3[] moved = new double3[count];
            for (int i = 0; i < count; i++) moved[i] = at[i] + (Vec.Cross(Vec.Unit(at[i]), Heading(at, i, 1, closed)) * left[i]);
            at = moved;
        }

        double[] along = new double[count];
        for (int i = 1; i < count; i++) along[i] = along[i - 1] + Vec.Len(at[i] - at[i - 1]);
        double total = closed ? along[count - 1] + Vec.Len(at[0] - at[count - 1]) : along[count - 1];

        double3[] heading = new double3[count];
        for (int i = 0; i < count; i++) heading[i] = Heading(at, i, 1, closed);

        int reach = Math.Max(1, (int)Math.Round(0.5 * SmoothOverM / (total / steps)));
        int rise = Math.Max(1, (int)Math.Round(VerticalOverM / (total / steps)));
        Sample[] samples = new Sample[count];
        for (int i = 0; i < count; i++)
        {
            int before = closed ? ((i - reach) % count + count) % count : Math.Max(i - reach, 0);
            int after = closed ? (i + reach) % count : Math.Min(i + reach, count - 1);
            double between = along[after] - along[before];
            if (closed && between <= 0.0) between += total;

            double3 up = Vec.Unit(at[i]);
            double turn = Math.Atan2(Vec.Dot(Vec.Cross(heading[before], heading[after]), up), Vec.Dot(heading[before], heading[after]));
            double climb = Vec.Len(at[after]) - Vec.Len(at[before]);

            // The line's length is along the road, so the climb over it is the slope's sine.
            double sine = between > 0.0 ? Math.Clamp(climb / between, -0.999, 0.999) : 0.0;
            double level = Math.Sqrt(1.0 - (sine * sine));
            double slope = sine / level;

            // Nothing at an end, where there is no road beyond to bend to.
            double vertical = 0.0;
            if (closed || (i >= rise && i < count - rise))
            {
                int low = Wrapped(i - rise), high = Wrapped(i + rise);
                double back = along[i] - along[low], on = along[high] - along[i];
                if (back <= 0.0) back += total;
                if (on <= 0.0) on += total;
                double bends = (((Vec.Len(at[high]) - Vec.Len(at[i])) / on) - ((Vec.Len(at[i]) - Vec.Len(at[low])) / back)) / (0.5 * (back + on));
                vertical = bends / level;
            }
            samples[i] = new Sample(at[i], along[i], heading[i], Vec.Cross(up, heading[i]),
                                    between > 0.0 ? turn / between : 0.0, slope, vertical, half[i], -left[i], bank[i]);
        }
        return new Route(samples, total, closed);
    }

    /// <summary>
    /// How far to the left of the centre line a car runs at each point to turn as little as it can
    /// with <paramref name="insideM"/> of road kept between it and either edge: out to the edge
    /// before a bend, across its inside and out again, which is what lets a bend be taken faster
    /// than its own radius allows.
    ///
    /// <para>The line whose points, a step apart, are nearest to lying straight: each is moved across
    /// the road to where the bend at it and at the two either side of it is least, over and over,
    /// first with the points far apart, where a whole bend is a few of them, and then closer.</para>
    /// </summary>
    public static double[] RacingLine(double3[] centre, double[] half, bool closed, double insideM)
    {
        int count = centre.Length;
        double3[] across = new double3[count];
        for (int i = 0; i < count; i++) across[i] = Vec.Cross(Vec.Unit(centre[i]), Heading(centre, i, 1, closed));

        double[] left = new double[count];
        for (int stride = 16; stride >= 1; stride /= 2)
        {
            int n = closed ? count / stride : ((count - 1) / stride) + 1;
            if (n < 6) continue;
            int At(int k) => closed ? ((k % n) + n) % n * stride : Math.Clamp(k, 0, n - 1) * stride;
            double3 Point(int k) => centre[At(k)] + (across[At(k)] * left[At(k)]);

            for (int sweep = 0; sweep < RaceSweeps; sweep++)
            {
                for (int k = closed ? 0 : 2; k < (closed ? n : n - 2); k++)
                {
                    int i = At(k);
                    double3 before = Point(k - 2) - (Point(k - 1) * 2.0) + centre[i];
                    double3 here = Point(k - 1) + Point(k + 1) - (centre[i] * 2.0);
                    double3 after = centre[i] - (Point(k + 1) * 2.0) + Point(k + 2);
                    double most = Math.Max(half[i] - insideM, 0.0);
                    left[i] = Math.Clamp(((2.0 * Vec.Dot(here, across[i])) - Vec.Dot(before, across[i]) - Vec.Dot(after, across[i])) / 6.0, -most, most);
                }
            }

            // The points between, for the next pass to start from.
            if (stride == 1) break;
            for (int k = 0; k < (closed ? n : n - 1); k++)
            {
                int a = At(k), b = At(k + 1);
                for (int j = 1; j < stride && a + j < count; j++) left[a + j] = left[a] + ((left[b] - left[a]) * j / stride);
            }
        }
        return left;
    }

    private const int RaceSweeps = 200;

    // The way the line runs at a point, level there: from the point before it to the one after.
    private static double3 Heading(double3[] at, int i, int reach, bool closed)
    {
        int count = at.Length;
        int before = closed ? (i - reach + count) % count : Math.Max(i - reach, 0);
        int after = closed ? (i + reach) % count : Math.Min(i + reach, count - 1);
        return Vec.Unit(Vec.RejectFrom(at[after] - at[before], at[i]));
    }

    /// <summary>The stretch of the route nearest a point, wherever on it that is: for a car that starts where it stands.</summary>
    public int Nearest(double3 point)
    {
        int best = 0;
        double least = double.PositiveInfinity;
        int last = Closed ? Count : Count - 1;
        for (int i = 0; i < last; i++)
        {
            double off = Off(point, i, out _, out _);
            if (off < least) (best, least) = (i, off);
        }
        return best;
    }

    /// <summary>
    /// Where a point is along the route, looking no further than <paramref name="aheadM"/> on from the
    /// stretch it was last on, <paramref name="index"/>, which is brought up to date. Answers true
    /// where that took it over the line that starts a closed route again.
    /// </summary>
    /// <param name="s">How far along, m.</param>
    /// <param name="cross">How far to the left of the route, m.</param>
    public bool Locate(double3 point, ref int index, double aheadM, out double s, out double cross)
    {
        int last = Closed ? Count : Count - 1;
        int best = index;
        double least = double.PositiveInfinity, gone = 0.0;
        s = cross = 0.0;
        for (int k = 0; k < last && gone <= aheadM; k++)
        {
            int i = index + k;
            if (i >= last)
            {
                if (!Closed) break;
                i -= last;
            }
            double off = Off(point, i, out double along, out double beside);
            if (off < least) (best, least, s, cross) = (i, off, _samples[i].S + along, beside);
            gone += Piece(i);
        }
        bool lapped = best < index;
        index = best;
        return lapped;
    }

    // How far a point is from a stretch, seen from above, squared; how far along it; and how far to its left.
    private double Off(double3 point, int i, out double along, out double beside)
    {
        Sample a = _samples[i];
        double3 b = _samples[(i + 1) % Count].At;
        double3 run = b - a.At;
        double length = Vec.Len(run);
        double3 from = Vec.RejectFrom(point - a.At, a.At);
        along = length > 0.0 ? Math.Clamp(Vec.Dot(from, run) / length, 0.0, length) : 0.0;
        double3 off = Vec.RejectFrom(from - (run * (length > 0.0 ? along / length : 0.0)), a.At);
        beside = Vec.Dot(off, a.Across);
        return Vec.Len2(off);
    }

    private double Piece(int i) => Vec.Len(_samples[(i + 1) % Count].At - _samples[i].At);

    /// <summary>
    /// The point <paramref name="distanceM"/> further along than <paramref name="s"/>, which is on the
    /// stretch <paramref name="index"/>. Past the end of a route that has one, straight on.
    /// </summary>
    public double3 Ahead(int index, double s, double distanceM)
    {
        int last = Closed ? Count : Count - 1;
        int i = index;
        double left = distanceM + (s - _samples[i].S);
        for (int guard = 0; guard < Count; guard++)
        {
            double piece = Piece(i);
            int next = (i + 1) % Count;
            if (left <= piece || (!Closed && i == last - 1))
            {
                return _samples[i].At + ((_samples[next].At - _samples[i].At) * (piece > 0.0 ? left / piece : 0.0));
            }
            left -= piece;
            i = next;
        }
        return _samples[i].At;
    }

    /// <summary>The sample at the start of the stretch a distance along is on.</summary>
    public int IndexAt(double s)
    {
        int last = Closed ? Count : Count - 1;
        int i = Math.Clamp((int)(s / LengthM * last), 0, last - 1);
        while (i > 0 && _samples[i].S > s) i--;
        while (i < last - 1 && _samples[i + 1].S <= s) i++;
        return i;
    }

    /// <summary>
    /// Where a car stands on the road a distance along the route: the place on the surface, the road's
    /// own up there and the way along it.
    /// </summary>
    public (double3 At, double3 Up, double3 Ahead) Standing(double s)
    {
        int i = IndexAt(s);
        Sample a = _samples[i];
        double3 at = Ahead(i, s, 0.0);
        double3 up = Vec.Unit(a.At);
        return (at, Vec.Unit(up - (a.Tangent * a.Slope)), Vec.Unit(a.Tangent + (up * a.Slope)));
    }
}
