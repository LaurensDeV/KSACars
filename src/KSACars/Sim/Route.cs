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
    /// <param name="LoopM">On a loop, how long the loop is: there the place is on the loop itself, the tangent is the way it really runs, up and over, and nothing of a road's slope or bend is said.</param>
    public readonly record struct Sample(double3 At, double S, double3 Tangent, double3 Across, double Curvature,
                                         double Slope, double Vertical, double HalfWidth, double CentreM, double Bank = 0.0, double LoopM = 0.0);

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
                            double turnRadiusM = 0.0, double raceInsideM = 0.0, IReadOnlyList<double>? nudgesM = null) =>
        Of(circuit, dirOf, radiusM, () => RoadLaying.Laid(circuit, dirOf, radiusM, groundAt, liftM, spacingM), spacingM, through, offsetM, out why,
           turnRadiusM, raceInsideM, nudgesM);

    /// <summary>The same over roads that are already laid, which reads no ground.</summary>
    public static Route? Of(Circuit circuit, Func<double, double, double3> dirOf, double radiusM, RoadLaying.Network network,
                            double spacingM, IReadOnlyList<int>? through, double offsetM, out string why,
                            double turnRadiusM = 0.0, double raceInsideM = 0.0, IReadOnlyList<double>? nudgesM = null) =>
        Of(circuit, dirOf, radiusM, () => network, spacingM, through, offsetM, out why, turnRadiusM, raceInsideM, nudgesM);

    private static Route? Of(Circuit circuit, Func<double, double, double3> dirOf, double radiusM, Func<RoadLaying.Network> network,
                             double spacingM, IReadOnlyList<int>? through, double offsetM, out string why,
                             double turnRadiusM, double raceInsideM, IReadOnlyList<double>? nudgesM)
    {
        why = "";
        List<int> path = through is { Count: > 0 } ? [.. through] : Following(circuit, dirOf, radiusM);
        if (path.Count < 2)
        {
            why = "a route needs two points joined by a road";
            return null;
        }

        RoadLaying.Network laid = network();
        List<RoadRibbon> ribbons = laid.Ribbons;
        List<double3> line = [];
        List<double> halfWidths = [], banks = [], shifts = [];

        // A loop is no road: the roads before it are one stretch, it is another, and the roads after it a third.
        List<Sample[]> stretches = [];
        bool looped = false;
        bool Stretch()
        {
            if (line.Count < 2) return line.Count == 0;
            if (Along([.. line], [.. halfWidths], false, offsetM, turnRadiusM, [.. banks], raceInsideM, null, [.. shifts]) is not { } part) return false;
            stretches.Add(part._samples);
            line.Clear();
            halfWidths.Clear();
            banks.Clear();
            shifts.Clear();
            return true;
        }

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
                shifts.Add(0.0);
            }
        }

        for (int k = 1; k < path.Count; k++)
        {
            int a = path[k - 1], b = path[k];
            if (k > 1 && line.Count > 0) Cross(path[k - 2], a, b);
            RoadRibbon? on = null;
            RoadRibbon.Span span = default;
            foreach (RoadRibbon ribbon in ribbons)
            {
                foreach (RoadRibbon.Span s in ribbon.Spans)
                {
                    if ((s.From == a && s.To == b) || (s.From == b && s.To == a)) (on, span) = (ribbon, s);
                }
            }
            if (on is null && circuit.Loops?.FirstOrDefault(l => l.From == a && l.To == b) is { } over)
            {
                if (RoadLoop.Of(over, ribbons, out why) is not { } loop) return null;
                if (!Stretch())
                {
                    why = "the road to a loop has no length";
                    return null;
                }
                int rows = Math.Max(2, (int)Math.Round(loop.LengthM / SpacingM));
                Sample[] round = new Sample[rows + 1];
                for (int i = 0; i <= rows; i++)
                {
                    double s = loop.LengthM * i / rows;
                    double3 place = loop.Point(s, 0.0, out double3 along, out _);
                    round[i] = new Sample(place, s, along, loop.Left, 0.0, 0.0, 0.0, loop.HalfWidthM, 0.0, 0.0, loop.LengthM);
                }
                stretches.Add(round);
                looped = true;
                continue;
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
                // The road's own line, and how far from it the middle of the asphalt is: that middle swerves
                // wherever the road widens to one side, and what is measured across from a line that swerves is nowhere it says.
                line.Add(on.Point(section, 0.0, section.Height));
                halfWidths.Add(section.HalfWidth);
                banks.Add(to >= from ? section.BankTan : -section.BankTan);
                shifts.Add(to >= from ? section.Shift : -section.Shift);
            }
        }

        bool closed = path.Count > 2 && path[0] == path[^1];
        if (looped)
        {
            // The last point of a lap is its first come round again.
            if (closed && line.Count > 1)
            {
                line.RemoveAt(line.Count - 1);
                halfWidths.RemoveAt(halfWidths.Count - 1);
                banks.RemoveAt(banks.Count - 1);
                shifts.RemoveAt(shifts.Count - 1);
            }
            if (!Stretch() || stretches.Count == 0)
            {
                why = "the route has no length";
                return null;
            }
            return Joined(stretches, closed);
        }
        if (closed)
        {
            Cross(path[^2], path[0], path[1]);
            line.RemoveAt(line.Count - 1);
            halfWidths.RemoveAt(halfWidths.Count - 1);
            banks.RemoveAt(banks.Count - 1);
            shifts.RemoveAt(shifts.Count - 1);
        }
        if (Along([.. line], [.. halfWidths], closed, offsetM, turnRadiusM, [.. banks], raceInsideM, nudgesM, [.. shifts]) is not { } route)
        {
            why = "the route has no length";
            return null;
        }
        return route;
    }

    // Stretches one after another as one route, each measured on from where the one before ended.
    private static Route Joined(List<Sample[]> stretches, bool closed)
    {
        List<Sample> all = [];
        double gone = 0.0;
        foreach (Sample[] stretch in stretches)
        {
            if (stretch.Length == 0) continue;
            if (all.Count > 0)
            {
                // A stretch starts where the last ended, give or take what a float makes of it: that place is said once.
                double gap = Vec.Len(stretch[0].At - all[^1].At);
                if (gap < 0.5 * SpacingM) all.RemoveAt(all.Count - 1);
                else gone += gap;
            }
            double from = all.Count > 0 ? gone : 0.0, first = stretch[0].S;
            foreach (Sample sample in stretch) all.Add(sample with { S = from + (sample.S - first) });
            gone = all[^1].S;
        }
        double length = gone + (closed ? Vec.Len(all[0].At - all[^1].At) : 0.0);
        return new Route([.. all], length, closed);
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
        for (int guard = 0; guard < 4 * (circuit.Roads.Count + 1); guard++)
        {
            if (RoadLayout.Through(circuit, at, path[^1], path[^2]) is { } onward)
            {
                if (!driven.Add((path[^1], onward))) break;
                path.Add(onward);
                continue;
            }

            // Where the road ends at a loop, round the loop and on by the road it comes down on.
            if (circuit.Loops?.FirstOrDefault(l => l.From == path[^1]) is not { } loop || !driven.Add((loop.From, loop.To))) break;
            if (circuit.Roads.FirstOrDefault(r => r.Touches(loop.To)) is not { } after) break;
            path.Add(loop.To);
            int next = after.From == loop.To ? after.To : after.From;
            if (!driven.Add((loop.To, next))) break;
            path.Add(next);
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
    /// <param name="middlesM">How far to the left of the line the middle of the asphalt is at each of its points, where the road is wider to one side: the route runs there, and a racing line keeps to the asphalt about it.</param>
    public static Route? Along(double3[] line, double[] halfWidths, bool closed, double offsetM, double turnRadiusM = 0.0,
                               double[]? banks = null, double raceInsideM = 0.0, IReadOnlyList<double>? nudgesM = null, double[]? middlesM = null)
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
        double[] bank = new double[count], mid = new double[count];
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
            if (middlesM is not null && middlesM.Length == line.Length) mid[i] = middlesM[piece] + ((middlesM[next] - middlesM[piece]) * t);
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

        double3[] drawn = kinks.Count > 0 ? [.. at] : at;
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
        for (int i = 0; i < count; i++) left[i] = mid[i] + offsetM;
        if (raceInsideM > 0.0)
        {
            // Round a kink the line so far is not where the road was drawn, and the asphalt is still about where it was drawn.
            double[] asphalt = mid;
            if (kinks.Count > 0)
            {
                asphalt = [.. mid];
                foreach ((int middle, int span, _, double outM) in kinks)
                {
                    int round = span + (outM > 0.0 ? (int)Math.Ceiling(Math.Max(SwingOver * outM, LeastSwingM) / each) : 0) + 1;
                    for (int j = -round; j <= round; j++)
                    {
                        if (!closed && (middle + j < 0 || middle + j >= count)) continue;
                        int i = Wrapped(middle + j);
                        asphalt[i] = mid[i] - LeftOf(drawn, at[i], middle + j - round, middle + j + round, closed);
                    }
                }
            }
            left = RacingLine(at, half, closed, raceInsideM, asphalt);

            // Moved to the left by as much as is asked at each of as many places evenly along it, and between
            // them in proportion: a line learnt, where the one that turns least is not the quickest. Still on the road.
            if (nudgesM is { Count: > 0 })
            {
                for (int i = 0; i < count; i++)
                {
                    double place = (double)i / count * nudgesM.Count;
                    int k = (int)place % nudgesM.Count, next = closed ? (k + 1) % nudgesM.Count : Math.Min(k + 1, nudgesM.Count - 1);
                    double most = Math.Max(half[i] - raceInsideM, 0.0);
                    left[i] = Math.Clamp(left[i] + nudgesM[k] + ((nudgesM[next] - nudgesM[k]) * (place - Math.Floor(place))), asphalt[i] - most, asphalt[i] + most);
                }
            }
        }
        if (offsetM != 0.0 || raceInsideM > 0.0 || Array.Exists(mid, m => m != 0.0))
        {
            // As high as the middle of the asphalt there, wherever across it the line is.
            double3[] moved = new double3[count];
            for (int i = 0; i < count; i++)
            {
                double3 up = Vec.Unit(at[i]);
                moved[i] = at[i] + (Vec.Cross(up, Heading(at, i, 1, closed)) * left[i]) + (up * (bank[i] * mid[i]));
            }
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
                                    between > 0.0 ? turn / between : 0.0, slope, vertical, half[i], mid[i] - left[i], bank[i]);
        }
        return new Route(samples, total, closed);
    }

    /// <summary>
    /// How far to the left of a line along the road a car runs at each point to turn as little as it
    /// can with <paramref name="insideM"/> of road kept between it and either edge: out to the edge
    /// before a bend, across its inside and out again, which is what lets a bend be taken faster
    /// than its own radius allows.
    ///
    /// <para>The line whose bend, squared and summed over its own length, is least. That is the bend
    /// the line itself has, seen from above, and not how far its points are from lying straight: on
    /// the inside of a bend the points are closer together than the road's, and a kink between
    /// points close together is hardly out of line at all.</para>
    ///
    /// <para>It is solved for and not relaxed towards: each round is the line that is best for the
    /// lengths and facings of the round before, with every point held inside its edges by a push
    /// that grows as the room runs out and is eased from round to round until only a point at an
    /// edge feels it. Some twenty rounds, each a sum over the points and five bands to solve.</para>
    /// </summary>
    /// <param name="middleM">How far to the left of the line the middle of the asphalt is at each point, or nothing where the line is it.</param>
    public static double[] RacingLine(double3[] centre, double[] half, bool closed, double insideM, double[]? middleM = null)
    {
        int n = centre.Length;
        double[] x = new double[n];
        if (n < 6) return x;
        int Wrap(int i) => ((i % n) + n) % n;

        double[] along = new double[n + 1];
        for (int i = 0; i < n; i++) along[i + 1] = along[i] + Vec.Len(centre[(i + 1) % n] - centre[i]);

        double3[] across = new double3[n];
        double[] low = new double[n], high = new double[n];
        bool[] free = new bool[n];
        int frees = 0;
        for (int i = 0; i < n; i++)
        {
            across[i] = Vec.Cross(Vec.Unit(centre[i]), Heading(centre, i, 1, closed));

            // A stretch with ends leaves and arrives in the middle of the road and along it.
            double most = closed || (i >= 2 && i < n - 2) ? Math.Max(half[i] - insideM, 0.0) : 0.0;
            double middle = middleM is null ? 0.0 : middleM[i];
            (low[i], high[i]) = (middle - most, middle + most);

            // Lines beside a bend run together towards the middle of its turn, and past it one would
            // go backwards: so no nearer that than leaves a line this share of the road's own length.
            int before = closed ? i - RaceFoldOver : Math.Max(i - RaceFoldOver, 0), after = closed ? i + RaceFoldOver : Math.Min(i + RaceFoldOver, n - 1);
            double3 from = Heading(centre, Wrap(before), 1, closed), to = Heading(centre, Wrap(after), 1, closed);
            double over = along[Wrap(after)] - along[Wrap(before)];
            if (after >= n || before < 0) over += along[n];
            double bend = over > 0.0 ? Math.Atan2(Vec.Dot(Vec.Cross(from, to), Vec.Unit(centre[i])), Vec.Dot(from, to)) / over : 0.0;
            double fold = (1.0 - RaceLeastApart) / Math.Max(Math.Abs(bend), 1e-9);
            if (bend > 0.0 && fold < high[i]) high[i] = Math.Max(fold, Math.Min(middle, high[i]));
            if (bend < 0.0 && -fold > low[i]) low[i] = Math.Min(-fold, Math.Max(middle, low[i]));

            x[i] = Math.Clamp(middle, low[i], high[i]);
            free[i] = high[i] - low[i] > RaceLeastRoomM;
            if (free[i]) frees++;
        }
        if (frees == 0) return x;

        double[] above = new double[n], below = new double[n], step = new double[n], upper = new double[n], lower = new double[n];
        double[] h0 = new double[n], h1 = new double[n], h2 = new double[n], g = new double[n];
        double[] m0 = new double[n], m1 = new double[n], m2 = new double[n];
        double[] d = new double[n], l1 = new double[n], l2 = new double[n], yp = new double[n], yq = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (!free[i]) continue;
            double room = high[i] - low[i];
            x[i] = Math.Clamp(x[i], low[i] + (RaceStartsIn * room), high[i] - (RaceStartsIn * room));
            above[i] = RaceHoldsOff / (high[i] - x[i]);
            below[i] = RaceHoldsOff / (x[i] - low[i]);
        }

        // A lap's last two points are joined to its first two, and without them the sums are five bands.
        int core = closed ? n - 2 : n;
        void Solve(double[] v)
        {
            for (int i = 0; i < core; i++)
            {
                if (i >= 1) v[i] -= l1[i - 1] * v[i - 1];
                if (i >= 2) v[i] -= l2[i - 2] * v[i - 2];
            }
            for (int i = core - 1; i >= 0; i--)
            {
                v[i] /= d[i];
                if (i + 1 < core) v[i] -= l1[i] * v[i + 1];
                if (i + 2 < core) v[i] -= l2[i] * v[i + 2];
            }
        }

        for (int round = 0; round < RaceRounds; round++)
        {
            // The bend at a point is what its neighbours are out of line by, across the line as it now
            // runs, over the square of how far apart they now are; and it counts for that far of line.
            Array.Clear(h0);
            Array.Clear(h1);
            Array.Clear(h2);
            Array.Clear(g);
            for (int i = closed ? 0 : 1; i < (closed ? n : n - 1); i++)
            {
                int p = Wrap(i - 1), q = Wrap(i + 1);
                double3 run = Vec.RejectFrom((centre[q] + (across[q] * x[q])) - (centre[p] + (across[p] * x[p])), centre[i]);
                double apart = 0.5 * Vec.Len(run);
                if (!(apart > 0.0)) continue;
                double3 normal = Vec.Cross(Vec.Unit(centre[i]), run * (0.5 / apart));
                apart = Math.Max(apart, RaceLeastApart * 0.5 * Vec.Len(centre[q] - centre[p]));
                double w = 1.0 / (apart * apart * apart);
                double a = Vec.Dot(normal, across[p]), b = -2.0 * Vec.Dot(normal, across[i]), c = Vec.Dot(normal, across[q]);
                double bent = Vec.Dot(normal, centre[p] - (centre[i] * 2.0) + centre[q]);
                h0[p] += w * a * a;
                h0[i] += w * b * b;
                h0[q] += w * c * c;
                h1[p] += w * a * b;
                h1[i] += w * b * c;
                h2[p] += w * a * c;
                g[p] += w * bent * a;
                g[i] += w * bent * b;
                g[q] += w * bent * c;
            }

            double holds = 0.0;
            for (int i = 0; i < n; i++)
            {
                if (free[i]) holds += (above[i] * (high[i] - x[i])) + (below[i] * (x[i] - low[i]));
            }
            holds /= 2 * frees;
            double aim = Math.Max(RaceEases * holds, RaceHeld);

            for (int i = 0; i < n; i++)
            {
                if (!free[i])
                {
                    (m0[i], m1[i], m2[i], step[i]) = (1.0, 0.0, 0.0, 0.0);
                    continue;
                }
                int a = Wrap(i - 2), b = Wrap(i - 1), c = Wrap(i + 1), e = Wrap(i + 2);
                double pull = g[i] + ((h0[i] + RaceToMiddle) * x[i]) + (h2[a] * x[a]) + (h1[b] * x[b]) + (h1[i] * x[c]) + (h2[i] * x[e]);
                double up = high[i] - x[i], down = x[i] - low[i];
                m0[i] = h0[i] + RaceToMiddle + (above[i] / up) + (below[i] / down);
                m1[i] = free[c] ? h1[i] : 0.0;
                m2[i] = free[e] ? h2[i] : 0.0;
                step[i] = -pull - (aim / up) + (aim / down);
            }

            for (int i = 0; i < core; i++)
            {
                d[i] = m0[i];
                if (i >= 1) d[i] -= l1[i - 1] * l1[i - 1] * d[i - 1];
                if (i >= 2) d[i] -= l2[i - 2] * l2[i - 2] * d[i - 2];
                l2[i] = i + 2 < core ? m2[i] / d[i] : 0.0;
                l1[i] = i + 1 < core ? (m1[i] - (i >= 1 ? l2[i - 1] * l1[i - 1] * d[i - 1] : 0.0)) / d[i] : 0.0;
            }
            Solve(step);
            if (closed)
            {
                // The last two from what is left of their own sums with the rest taken out, and the rest from those.
                int p = n - 2, q = n - 1;
                Array.Clear(yp);
                Array.Clear(yq);
                (yp[n - 4], yp[n - 3], yp[0]) = (m2[n - 4], m1[n - 3], m2[p]);
                (yq[n - 3], yq[0], yq[1]) = (m2[n - 3], m1[q], m2[q]);
                Solve(yp);
                Solve(yq);
                double WithP(double[] v) => (m2[n - 4] * v[n - 4]) + (m1[n - 3] * v[n - 3]) + (m2[p] * v[0]);
                double WithQ(double[] v) => (m2[n - 3] * v[n - 3]) + (m1[q] * v[0]) + (m2[q] * v[1]);
                double pp = m0[p] - WithP(yp), pq = m1[p] - WithP(yq), qq = m0[q] - WithQ(yq);
                double rp = step[p] - WithP(step), rq = step[q] - WithQ(step);
                double det = (pp * qq) - (pq * pq);
                (step[p], step[q]) = (((qq * rp) - (pq * rq)) / det, ((pp * rq) - (pq * rp)) / det);
                for (int i = 0; i < core; i++) step[i] -= (yp[i] * step[p]) + (yq[i] * step[q]);
            }

            // As far as leaves every point inside its edges and every push a push.
            double far = 1.0, farPush = 1.0;
            for (int i = 0; i < n; i++)
            {
                if (!free[i]) continue;
                double up = high[i] - x[i], down = x[i] - low[i];
                if (step[i] > 0.0) far = Math.Min(far, RaceShortOf * up / step[i]);
                if (step[i] < 0.0) far = Math.Min(far, RaceShortOf * down / -step[i]);
                upper[i] = (aim / up) - above[i] + (above[i] / up * step[i]);
                lower[i] = (aim / down) - below[i] - (below[i] / down * step[i]);
                if (upper[i] < 0.0) farPush = Math.Min(farPush, RaceShortOf * above[i] / -upper[i]);
                if (lower[i] < 0.0) farPush = Math.Min(farPush, RaceShortOf * below[i] / -lower[i]);
            }
            double went = 0.0;
            for (int i = 0; i < n; i++)
            {
                if (!free[i]) continue;
                x[i] += far * step[i];
                above[i] += farPush * upper[i];
                below[i] += farPush * lower[i];
                went = Math.Max(went, Math.Abs(far * step[i]));
            }
            if (went < RaceSettledM && holds < 2.0 * RaceHeld) break;
        }
        return x;
    }

    // The most rounds a line is given, and how little its furthest-moved point moves in the round that is its last, m.
    private const int RaceRounds = 60;
    private const double RaceSettledM = 0.005;

    // The least a line's points may be apart, as a share of the road's own, and how many points either side a road's bend is measured over for it.
    private const double RaceLeastApart = 0.5;
    private const int RaceFoldOver = 3;

    // A point with less room than this between its edges is not moved, m; one that is starts no nearer an edge than this share of its room.
    private const double RaceLeastRoomM = 0.01, RaceStartsIn = 0.1;

    // The push off an edge is this over the room left, to start with; each round it is eased to this share of itself, and no
    // further than to this, which holds a point that leans on an edge some hundredth of a millimetre off it; and a round goes
    // no more than this share of the way to an edge, or to no push at all.
    private const double RaceHoldsOff = 0.05, RaceEases = 0.2, RaceHeld = 1e-7, RaceShortOf = 0.95;

    // What draws a point to the middle of the road where no bend says where across it to be: next to nothing, and without it a straight road has no answer.
    private const double RaceToMiddle = 1e-9;

    // How far to the left of a line of points a place is, by the nearest of the stretches between two of them.
    private static double LeftOf(double3[] line, double3 place, int from, int to, bool closed)
    {
        int count = line.Length;
        double least = double.PositiveInfinity, left = 0.0;
        for (int k = from; k < to; k++)
        {
            if (!closed && (k < 0 || k + 1 >= count)) continue;
            double3 a = line[((k % count) + count) % count], b = line[(((k + 1) % count) + count) % count];
            double3 run = Vec.RejectFrom(b - a, a), off = Vec.RejectFrom(place - a, a);
            double length2 = Vec.Len2(run);
            double3 away = off - (run * (length2 > 0.0 ? Math.Clamp(Vec.Dot(off, run) / length2, 0.0, 1.0) : 0.0));
            double far = Vec.Len2(away);
            if (far >= least) continue;
            least = far;
            left = Math.Sqrt(far) * Math.Sign(Vec.Dot(Vec.Cross(Vec.Unit(a), run), away));
        }
        return left;
    }

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
        // A loop is upright, and seen from above half of it is no length at all: there, as it is.
        bool loop = a.LoopM > 0.0;
        double3 from = loop ? point - a.At : Vec.RejectFrom(point - a.At, a.At);
        along = length > 0.0 ? Math.Clamp(Vec.Dot(from, run) / length, 0.0, length) : 0.0;
        double3 away = from - (run * (length > 0.0 ? along / length : 0.0));
        double3 off = loop ? away : Vec.RejectFrom(away, a.At);
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
        if (a.LoopM > 0.0) return (at, Vec.Cross(a.Tangent, a.Across), a.Tangent);
        return (at, Vec.Unit(up - (a.Tangent * a.Slope)), Vec.Unit(a.Tangent + (up * a.Slope)));
    }
}
