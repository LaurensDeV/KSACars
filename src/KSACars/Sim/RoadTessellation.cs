using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// One stretch of a road as the vertices and triangles of a mesh, in the body's own frame.
///
/// <para>Positions are from <paramref name="Origin"/>, which is from the body's centre, so that none
/// is more than the stretch's own size and a float holds it to a hundredth of a millimetre. A
/// triangle's corners go round anticlockwise seen from outside the road. The triangles come asphalt
/// first, then the verge, the embankment and whatever closes an end of them, then a deck's sides
/// and underside, so each can be drawn with a material of its own.</para>
/// </summary>
/// <param name="Uvs">The asphalt's are its place on the circuit's chart over <see cref="RoadTessellation.TileM"/>, so they have no seam anywhere.</param>
/// <param name="AsphaltIndices">How many of <paramref name="Indices"/> are the asphalt's; and so for the earth's and the deck's after them.</param>
/// <param name="RadiusM">The furthest any vertex is from the origin.</param>
/// <param name="Folded">How many triangles were left out for facing away from their own vertices: the inside of a bend tighter than the road is wide.</param>
/// <param name="StartRow">The left edge, the centre and the right edge of the asphalt where the stretch starts, from the body's centre, as they were before they were narrowed to floats.</param>
/// <param name="Places">Every vertex from the body's centre, before it was narrowed: two meshes' vertices on the row they share are the same here to the last bit.</param>
internal sealed record RoadMeshData(
    double3 Origin, float3[] Positions, float3[] Normals, float2[] Uvs, int[] Indices,
    int AsphaltIndices, int EarthIndices, int DeckIndices, double RadiusM, double FromS, double ToS,
    double3[] StartRow, double3[] EndRow, int Folded, double3[] Places, int TrimIndices = 0);

/// <summary>
/// A <see cref="RoadRibbon"/> as triangles: rows of vertices across the road at stations along it,
/// joined up.
///
/// <para>The stations are as far apart as the road's shape allows and no further: where it turns,
/// climbs or twists harder they are closer, so that no triangle is more than
/// <see cref="PlanToleranceM"/> to the side of the surface or <see cref="HeightToleranceM"/> above or
/// below it, and the road does not turn more than <see cref="MostTurnRad"/> from one to the next. A
/// straight, level road has one every <see cref="MostStepM"/>.</para>
///
/// <para>A row's vertices are shared by the triangles either side of it, so nothing has an end to
/// show. A vertex is given twice only where the surface has a crease, at the asphalt's edge, where
/// the verge meets the bank and round a deck's corners, and each has the facing of its own side:
/// shading is smooth across a face and sharp at a crease.</para>
/// </summary>
internal static class RoadTessellation
{
    public const double PlanToleranceM = 0.005, HeightToleranceM = 0.003;
    public const double LeastStepM = 0.5, MostStepM = 10.0;
    public static readonly double MostTurnRad = Math.PI / 180.0;

    /// <summary>About how long a stretch of road one mesh is of.</summary>
    public const double ChunkM = 100.0;

    /// <summary>How far the texture goes before it repeats (m).</summary>
    public const double TileM = 4.0;

    /// <summary>A triangle smaller than this is left out: a sliver where a verge has no width.</summary>
    public const double LeastAreaM2 = 1e-4;

    /// <summary>A row across the road: how far along, and the road there. At a kink there are several at one distance, each turned a little further.</summary>
    public readonly record struct Station(double S, RoadRibbon.Section At);

    /// <param name="Stretches">Each two stations that triangles are put between, in order along the run.</param>
    public sealed record Layout(Station[] Stations, (int From, int To)[] Stretches);

    /// <summary>Where the rows of a ribbon's mesh go.</summary>
    public static Layout Stations(RoadRibbon ribbon)
    {
        RoadLine line = ribbon.Line;
        List<double> forced = [];
        int chunks = Math.Max(1, (int)Math.Round(ribbon.LengthM / ChunkM));
        for (int k = 1; k < chunks; k++) forced.Add(ribbon.LengthM * k / chunks);
        for (int i = 1; i < ribbon.SideStretches; i++)
        {
            if (ribbon.DeckOver(i - 1) != ribbon.DeckOver(i)) forced.Add(i * ribbon.SideStepM);
        }
        forced.Sort();

        List<Station> stations = [];
        List<(int, int)> stretches = [];
        void Add(Station station)
        {
            stations.Add(station);
            if (stations.Count > 1) stretches.Add((stations.Count - 2, stations.Count - 1));
        }

        for (int arc = 0; arc < line.Count; arc++)
        {
            double from = line.StartOf(arc), to = line.StartOf(arc + 1);
            if (arc == 0) Add(new Station(from, ribbon.At(arc, from)));
            else Fan(ribbon, arc, ribbon.At(arc - 1, from).Heading, Add);

            double s = from;
            int next = 0;
            while (s < to)
            {
                while (next < forced.Count && forced[next] <= s + 1e-6) next++;
                double target = next < forced.Count && forced[next] < to - 1e-6 ? forced[next] : to;
                s = Math.Min(s + Step(ribbon, arc, s, target - s), target);
                if (target - s < 1e-6) s = target;

                // A closed run ends on its own first row, unless it has a kink there to turn through.
                if (line.Closed && arc == line.Count - 1 && s >= to && Math.Abs(line.TurnAt(0)) <= RoadLine.KinkRad)
                {
                    stretches.Add((stations.Count - 1, 0));
                    break;
                }
                Add(new Station(s, ribbon.At(arc, s)));
            }
        }
        if (line.Closed && Math.Abs(line.TurnAt(0)) > RoadLine.KinkRad)
        {
            // The fan's last row is the run's first, come round to again.
            Fan(ribbon, 0, stations[^1].At.Heading, Add, to: line.LengthM);
            stretches[^1] = (stretches[^1].Item1, 0);
            stations.RemoveAt(stations.Count - 1);
        }
        return new Layout([.. stations], [.. stretches]);
    }

    // Round the outside of a kink the road is a fan about the point, as a wheel is told it is: rows
    // at one place, each turned a little further, and the last of them the row the next curve starts from.
    private static void Fan(RoadRibbon ribbon, int arc, Plan before, Action<Station> add, double? to = null)
    {
        double s = to ?? ribbon.Line.StartOf(arc);
        RoadRibbon.Section at = ribbon.At(arc, ribbon.Line.StartOf(arc)) with { S = s };
        double turn = Math.Atan2(Plan.Cross(before, at.Heading), Plan.Dot(before, at.Heading));
        if (Math.Abs(turn) <= RoadLine.KinkRad) return;

        double each = Math.Sqrt(8.0 * PlanToleranceM / (at.HalfWidth + RoadRibbon.VergeM));
        int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(turn) / each));
        for (int j = 1; j <= steps; j++)
        {
            double angle = turn * j / steps, cos = Math.Cos(angle), sin = Math.Sin(angle);
            Plan heading = j == steps ? at.Heading : new Plan((before.E * cos) - (before.N * sin), (before.E * sin) + (before.N * cos));
            add(new Station(s, at with { Heading = heading, Curvature = 0.0 }));
        }
    }

    // How far on the next row can be: what the road's shape says there, and then no further than
    // the middle of the stretch is measured to allow.
    private static double Step(RoadRibbon ribbon, int arc, double s, double room)
    {
        RoadRibbon.Section at = ribbon.At(arc, s);
        double step = MostStepM, squeezed = Math.Abs(at.Curvature) * at.HalfWidth;

        // The inside edge turns harder than the centre line, by how much of the bend's radius the half width is.
        double edge = squeezed < 0.95 ? Math.Abs(at.Curvature) / (1.0 - squeezed) : double.PositiveInfinity;
        if (edge > 0.0) step = Math.Min(step, Math.Min(Math.Sqrt(8.0 * PlanToleranceM / edge), MostTurnRad / edge));
        if (Math.Abs(at.Bend) > 0.0) step = Math.Min(step, Math.Sqrt(8.0 * HeightToleranceM / Math.Abs(at.Bend)));
        double twist = at.HalfWidth * Math.Abs(at.BankRate);
        if (twist > 0.0) step = Math.Min(step, Math.Sqrt(8.0 * HeightToleranceM / twist));
        step = Math.Clamp(step, LeastStepM, MostStepM);

        step = Math.Min(step, room);
        while (step > LeastStepM && !Within(ribbon, arc, at, step))
        {
            step = Math.Max(0.5 * step, LeastStepM);
        }

        // Not all but the last sliver of the way to a row that has to be there: half of it, and half again.
        return room <= 1.001 * step ? room : room < 2.0 * step ? 0.5 * room : step;
    }

    // The road a quarter, a half and three quarters of the way along a stretch, against the straight
    // line between its two rows: at the centre and at both edges, to the side and in height. With a
    // fifth to spare, for what lies between the three places looked at.
    private static bool Within(RoadRibbon ribbon, int arc, in RoadRibbon.Section from, double step)
    {
        RoadRibbon.Section to = ribbon.At(arc, from.S + step);
        double turn = Math.Abs(Math.Atan2(Plan.Cross(from.Heading, to.Heading), Plan.Dot(from.Heading, to.Heading)));
        double squeezed = Math.Max(Math.Abs(from.Curvature) * from.HalfWidth, Math.Abs(to.Curvature) * to.HalfWidth);
        if (squeezed < 0.95 && turn / (1.0 - squeezed) > MostTurnRad) return false;

        // And the way it faces, which a crest or a change of lean turns where the road runs straight.
        for (int side = -1; side <= 1; side++)
        {
            if (Vec.AngleBetween(Facing(ribbon, from, side * from.HalfWidth), Facing(ribbon, to, side * to.HalfWidth)) > MostTurnRad) return false;
        }

        for (int k = 1; k <= 3; k++)
        {
            double t = 0.25 * k;
            RoadRibbon.Section at = ribbon.At(arc, from.S + (step * t));
            for (int side = -1; side <= 1; side++)
            {
                Plan a = from.At + (from.Left * (side * from.HalfWidth)), b = to.At + (to.Left * (side * to.HalfWidth));
                Plan on = at.At + (at.Left * (side * at.HalfWidth));
                Plan off = on - (a + ((b - a) * t));
                if (Math.Abs(Plan.Dot(off, at.Left)) > 0.8 * PlanToleranceM) return false;

                double ha = from.Height + (side * from.HalfWidth * from.BankTan), hb = to.Height + (side * to.HalfWidth * to.BankTan);
                double h = at.Height + (side * at.HalfWidth * at.BankTan);
                if (Math.Abs(h - (ha + ((hb - ha) * t))) > 0.8 * HeightToleranceM) return false;
            }
        }

        // And the middle of each line a stretch is cut into triangles along, from one row's edge to the
        // next row's centre. Round a bend two rows are not parallel, so that middle is not halfway
        // along the road, and where the road climbs or its lean changes it is not at the height
        // halfway between the two either.
        RoadRibbon.Section middle = ribbon.At(arc, from.S + (0.5 * step));
        ReadOnlySpan<(double From, double To)> cuts = [(from.HalfWidth, 0.0), (0.0, -to.HalfWidth)];
        foreach ((double da, double db) in cuts)
        {
            Plan off = (((from.At + (from.Left * da)) + (to.At + (to.Left * db))) * 0.5) - middle.At;
            double along = Plan.Dot(off, middle.Heading), across = Plan.Dot(off, middle.Left);
            double squeeze = Math.Max(1.0 - (middle.Curvature * across), 0.05);
            double surface = middle.Height + (across * middle.BankTan) + (along * (middle.Slope + (across * middle.BankRate)) / squeeze);
            double mesh = 0.5 * (from.Height + (da * from.BankTan) + to.Height + (db * to.BankTan));
            if (Math.Abs(mesh - surface) > 0.8 * HeightToleranceM) return false;
        }
        return true;
    }

    private static double3 Facing(RoadRibbon ribbon, in RoadRibbon.Section at, double d) =>
        ribbon.Normal(at, d, at.Height + (d * at.BankTan), at.Slope + (d * at.BankRate), at.BankTan);

    private enum Kind
    {
        Asphalt,
        Earth,
        Deck,

        /// <summary>What is put beside a road for the eye and for a car that has left it: a kerb round a bend, a barrier along a deck. Last of a mesh's triangles.</summary>
        Trim,
    }

    private sealed record Row(double3[] At, double3[] Normal, float2[] Uv);

    // Across a road on the ground, left to right: the bank's foot and its top, the verge's two edges,
    // the asphalt's edge, centre and other edge, and the same going down the right. Each crease twice.
    private static readonly (int Left, Kind Kind)[] GroundStrips =
        [(0, Kind.Earth), (2, Kind.Earth), (4, Kind.Asphalt), (5, Kind.Asphalt), (7, Kind.Earth), (9, Kind.Earth)];

    // The same round a bend, where each verge is a kerb.
    private static readonly (int Left, Kind Kind)[] KerbedStrips =
        [(0, Kind.Earth), (2, Kind.Trim), (4, Kind.Asphalt), (5, Kind.Asphalt), (7, Kind.Trim), (9, Kind.Earth)];

    /// <summary>A bend tighter than this has kerbs: one a racing car has to slow for.</summary>
    public const double KerbRadiusM = 150.0;

    /// <summary>A deck's barrier: how high it stands over the road's edge and how thick it is, outside that edge.</summary>
    public const double BarrierHighM = 0.9, BarrierThickM = 0.2;

    /// <summary>The least length of a barrier's panel, which is straight: a row a panel would be nine times the triangles round a tight bend.</summary>
    public const double BarrierStepM = 3.0;

    /// <summary>How far along a road a kerb's two colours and a barrier's panel repeat.</summary>
    public const double TrimRepeatM = 1.5;

    // Where in the trim's picture a kerb is, across it, and where a barrier is: its left half is the kerb's stripes.
    private const double KerbFrom = 0.03, KerbTo = 0.47, BarrierFrom = 0.53, BarrierTo = 0.97;

    // Round a deck: over the top left to right, down the right side, back under it and up the left.
    private static readonly (int Left, Kind Kind)[] DeckStrips =
        [(0, Kind.Asphalt), (1, Kind.Asphalt), (3, Kind.Deck), (5, Kind.Deck), (7, Kind.Deck)];

    private static double Lateral(in RoadRibbon.Section at, double d)
    {
        double beside = Math.Abs(d) - at.HalfWidth;
        return beside > 0.0 ? at.Height + (Math.Sign(d) * at.HalfWidth * at.BankTan) - RoadRibbon.Drop(beside) : at.Height + (d * at.BankTan);
    }

    // A row of a road on the ground, moved `beyond` on past the end of its run and sunk by what the
    // surface falls in that distance; `alongFall` is then the fall it faces, and otherwise nothing.
    private static Row GroundRow(RoadRibbon ribbon, in RoadRibbon.Section section, double beyond = 0.0, double alongFall = 0.0)
    {
        double ahead = section.S > 0.5 * ribbon.LengthM ? 1.0 : -1.0;
        RoadRibbon.Section at = beyond != 0.0 || alongFall != 0.0 ? section with { At = section.At + (section.Heading * (ahead * beyond)) } : section;
        double half = at.HalfWidth, vergeLeft = Math.Min(at.ToeLeft, RoadRibbon.VergeM), vergeRight = Math.Min(at.ToeRight, RoadRibbon.VergeM);
        ReadOnlySpan<double> across =
        [
            half + at.ToeLeft, half + vergeLeft, half + vergeLeft, half, half, 0.0,
            -half, -half, -half - vergeRight, -half - vergeRight, -half - at.ToeRight,
        ];
        ReadOnlySpan<double> falls =
        [
            RoadRibbon.BankSlope, RoadRibbon.BankSlope, RoadRibbon.VergeSlope, RoadRibbon.VergeSlope, 0.0, 0.0,
            0.0, RoadRibbon.VergeSlope, RoadRibbon.VergeSlope, RoadRibbon.BankSlope, RoadRibbon.BankSlope,
        ];

        Row row = new(new double3[11], new double3[11], new float2[11]);
        for (int i = 0; i < 11; i++)
        {
            double d = across[i], side = i < 5 ? 1.0 : -1.0, height = Lateral(at, d) - RoadRibbon.Drop(beyond);
            double alongRise = at.Slope + (d * at.BankRate), acrossRise = at.BankTan;
            if (falls[i] > 0.0) RoadRibbon.SideRises(at, side, falls[i], out alongRise, out acrossRise);
            if (alongFall != 0.0) alongRise = -ahead * alongFall;

            row.At[i] = ribbon.Point(at, d, height);
            row.Normal[i] = ribbon.Normal(at, d, height, alongRise, acrossRise);

            // Past a run's end the rows are all of one place along it, and earth: only a row of the run itself is lined.
            // A verge runs with the road too, edge to edge of the trim's kerb, so a bend can have it for a kerb.
            bool run = beyond == 0.0 && alongFall == 0.0;
            row.Uv[i] = run && i is >= 4 and <= 6 ? Lined(ribbon, section, 0.5 * (i - 4))
                      : run && i is 2 or 3 or 7 or 8 ? new float2((float)(i is 3 or 7 ? KerbFrom : KerbTo), (float)(Drawn(ribbon, section, out _) / TrimRepeatM))
                      : Tile(at.At + (at.Left * d));
        }
        return row;
    }

    private static Row DeckRow(RoadRibbon ribbon, in RoadRibbon.Section at)
    {
        double half = at.HalfWidth;
        ReadOnlySpan<double> across = [half, 0.0, -half, -half, -half, -half, half, half, half];
        ReadOnlySpan<bool> under = [false, false, false, false, true, true, true, true, false];

        Row row = new(new double3[9], new double3[9], new float2[9]);
        for (int i = 0; i < 9; i++)
        {
            double d = across[i], top = at.Height + (d * at.BankTan), height = under[i] ? top - RoadRibbon.DeckThickM : top;
            Plan place = at.At + (at.Left * d);
            ribbon.Chart.Compass(place, out _, out double3 east, out double3 north);
            double3 left = (east * at.Left.E) + (north * at.Left.N);
            double3 facing = ribbon.Normal(at, d, top, at.Slope + (d * at.BankRate), at.BankTan);

            row.At[i] = ribbon.Point(at, d, height);
            row.Normal[i] = i <= 2 ? facing : i <= 4 ? -left : i <= 6 ? -facing : left;

            // A side is upright, so the chart has no width of it: along whichever of east and north it runs nearer, and up.
            row.Uv[i] = i <= 2 ? Lined(ribbon, at, 0.5 * i) : i is 5 or 6 ? Tile(place)
                      : new float2((float)((Math.Abs(at.Heading.E) >= Math.Abs(at.Heading.N) ? place.E : place.N) / TileM), (float)(height / TileM));
        }
        return row;
    }

    private static float2 Tile(Plan place) => new((float)(place.E / TileM), (float)(place.N / TileM));

    /// <summary>How far along a road its markings repeat: a dash and the gap after it.</summary>
    public const double MarkingsM = 12.0;

    // A run's asphalt is drawn in a picture that knows which way the road goes: across it from its left
    // edge to its right, whatever its width, and along it by its own length, so its edge lines and the
    // dashes down its middle follow it. Round a closed run the dashes are stretched to come out whole.
    private static float2 Lined(RoadRibbon ribbon, in RoadRibbon.Section at, double across)
    {
        double drawn = Drawn(ribbon, at, out double whole);
        double repeat = ribbon.Closed ? whole / Math.Max(1.0, Math.Round(whole / MarkingsM)) : MarkingsM;
        return new float2((float)across, (float)(drawn / repeat));
    }

    // How far along a run a row is for what is drawn along it, and how long the whole run is by that measure.
    // Round a kink the rows are all at one distance along, each turned a little further, and a picture
    // laid by that distance alone would have no length there: the kink is taken to be as long as its
    // outside edge is, and every row after it that much further on.
    private static double Drawn(RoadRibbon ribbon, in RoadRibbon.Section at, out double whole)
    {
        RoadLine line = ribbon.Line;
        double drawn = at.S;
        whole = line.LengthM;
        for (int arc = line.Closed ? 0 : 1; arc < line.Count; arc++)
        {
            double turn = Math.Abs(line.TurnAt(arc));
            if (turn <= RoadLine.KinkRad) continue;

            double outside = at.HalfWidth * turn, kinkAt = arc == 0 ? line.LengthM : line.StartOf(arc);
            whole += outside;
            if (at.S > kinkAt + 1e-9) drawn += outside;
            else if (Math.Abs(at.S - kinkAt) <= 1e-9)
            {
                Plan after = ribbon.At(arc, line.StartOf(arc)).Heading;
                double left = Math.Abs(Math.Atan2(Plan.Cross(at.Heading, after), Plan.Dot(at.Heading, after)));
                drawn += at.HalfWidth * (turn - Math.Min(left, turn));
            }
        }
        return drawn;
    }

    // A wall along each edge of a deck between two rows, outside the edge: its face to the road, its top
    // and its back down to the deck's underside. Vertices of its own, since each face is flat.
    private static void Barrier(Chunk chunk, RoadRibbon ribbon, in RoadRibbon.Section from, in RoadRibbon.Section to)
    {
        double vFrom = Drawn(ribbon, from, out _) / TrimRepeatM, vTo = Drawn(ribbon, to, out _) / TrimRepeatM;
        if (Math.Abs(vTo - vFrom) < 1e-6) return;

        for (int side = -1; side <= 1; side += 2)
        {
            // Up the face from the road's edge, over the top and down the back: four places across, at each row.
            ReadOnlySpan<(double Out, double Up)> across =
                [(0.0, 0.0), (0.0, BarrierHighM), (BarrierThickM, BarrierHighM), (BarrierThickM, -RoadRibbon.DeckThickM)];
            Span<double3> a = stackalloc double3[4], b = stackalloc double3[4];
            Span<double> u = stackalloc double[4];
            double span = (2.0 * BarrierHighM) + BarrierThickM + RoadRibbon.DeckThickM, gone = 0.0;
            for (int k = 0; k < 4; k++)
            {
                if (k > 0) gone += Math.Abs(across[k].Out - across[k - 1].Out) + Math.Abs(across[k].Up - across[k - 1].Up);
                u[k] = BarrierFrom + ((BarrierTo - BarrierFrom) * gone / span);
                a[k] = At(ribbon, from, side, across[k].Out, across[k].Up);
                b[k] = At(ribbon, to, side, across[k].Out, across[k].Up);
            }
            for (int k = 0; k < 3; k++)
            {
                double3 along = b[k] - a[k], up = a[k + 1] - a[k];
                double3 facing = Vec.Unit(Vec.Cross(along, up) * side);
                int first = chunk.Add(a[k], facing, new float2((float)u[k], (float)vFrom));
                chunk.Add(a[k + 1], facing, new float2((float)u[k + 1], (float)vFrom));
                chunk.Add(b[k + 1], facing, new float2((float)u[k + 1], (float)vTo));
                chunk.Add(b[k], facing, new float2((float)u[k], (float)vTo));
                chunk.Triangle(first, first + 1, first + 2, Kind.Trim, turn: true);
                chunk.Triangle(first, first + 2, first + 3, Kind.Trim, turn: true);
            }
        }

        static double3 At(RoadRibbon ribbon, in RoadRibbon.Section at, int side, double beyond, double over)
        {
            double d = side * (at.HalfWidth + beyond);
            return ribbon.Point(at, d, at.Height + (side * at.HalfWidth * at.BankTan) + over);
        }
    }

    // The most a deck's barrier adds to a stretch: three faces a side, each with four vertices of its own.
    private const int BarrierVertices = 24, BarrierIndices = 36;

    // One mesh as it is put together: vertices from the body's centre, and the triangles of each kind.
    private sealed class Chunk
    {
        public readonly List<double3> At = [], Normal = [];
        public readonly List<float2> Uv = [];
        public readonly List<int>[] Triangles = [[], [], [], []];
        public readonly Dictionary<Row, int> Rows = new(ReferenceEqualityComparer.Instance);
        public int Folded;

        public int Add(Row row)
        {
            if (Rows.TryGetValue(row, out int at)) return at;
            at = At.Count;
            At.AddRange(row.At);
            Normal.AddRange(row.Normal);
            Uv.AddRange(row.Uv);
            Rows[row] = at;
            return at;
        }

        public int Add(double3 at, double3 normal, float2 uv)
        {
            At.Add(at);
            Normal.Add(normal);
            Uv.Add(uv);
            return At.Count - 1;
        }

        // Left out where it is a sliver, and where it faces away from its own vertices: the inside of a
        // bend tighter than the road is wide, where the surface has folded over itself.
        public void Triangle(int a, int b, int c, Kind kind, bool turn = false)
        {
            double3 cross = Vec.Cross(At[b] - At[a], At[c] - At[a]);
            if (0.5 * Vec.Len(cross) < LeastAreaM2) return;
            if (Vec.Dot(cross, Normal[a] + Normal[b] + Normal[c]) < 0.0)
            {
                if (!turn)
                {
                    Folded++;
                    return;
                }
                (b, c) = (c, b);
            }
            Triangles[(int)kind].AddRange([a, b, c]);
        }

        public void Between(Row from, Row to, (int Left, Kind Kind)[] strips)
        {
            int a = Add(from), b = Add(to);
            foreach ((int left, Kind kind) in strips)
            {
                Triangle(a + left, a + left + 1, b + left + 1, kind);
                Triangle(a + left, b + left + 1, b + left, kind);
            }
        }

        // A flat end: a fan from the first of its corners, facing along the road one way or the other.
        // A wall hung from a line of points down to the straight line between its two ends: each
        // stretch of the line a strip under it, so nothing of the wall is above the line anywhere.
        public void Curtain(double3[] line, float2[] uvs, double3 facing, Kind kind)
        {
            int first = At.Count, last = line.Length - 1;
            for (int i = 0; i <= last; i++) Add(line[i], facing, uvs[i]);
            for (int i = 0; i <= last; i++)
            {
                double t = Math.Abs(uvs[last].X - uvs[0].X) > 1e-9 ? (uvs[i].X - uvs[0].X) / (uvs[last].X - uvs[0].X) : (double)i / last;
                Add(line[0] + ((line[last] - line[0]) * t), facing, new float2(uvs[i].X, (float)(uvs[0].Y + ((uvs[last].Y - uvs[0].Y) * t))));
            }
            int feet = first + line.Length;
            for (int i = 0; i < last; i++)
            {
                if (i > 0) Triangle(first + i + 1, first + i, feet + i, kind, turn: true);
                if (i + 1 < last) Triangle(first + i + 1, feet + i, feet + i + 1, kind, turn: true);
            }
        }

        public void Cap(double3[] corners, float2[] uvs, double3 facing, Kind kind)
        {
            int first = At.Count;
            for (int i = 0; i < corners.Length; i++) Add(corners[i], facing, uvs[i]);
            for (int i = 1; i + 1 < corners.Length; i++) Triangle(first, first + i, first + i + 1, kind, turn: true);
        }
    }

    /// <summary>A ribbon as meshes, each of about <see cref="ChunkM"/> of it.</summary>
    public static List<RoadMeshData> Mesh(RoadRibbon ribbon) => Mesh(ribbon, Stations(ribbon));

    /// <summary>
    /// The most one mesh may be: what the room kept for it holds, and how long a stretch of road,
    /// which keeps a float exact and a mesh worth leaving out on its own when it is far off.
    /// </summary>
    public readonly record struct Fit(int Vertices, int Indices, double LengthM);

    /// <summary>
    /// A ribbon as meshes none of which is more than <paramref name="fit"/>, each as much of the road
    /// as that holds: few where it runs straight and many where it winds. The same triangles as the
    /// meshes by length, shared out differently.
    /// </summary>
    public static List<RoadMeshData> Mesh(RoadRibbon ribbon, Fit fit) => Mesh(ribbon, Stations(ribbon), fit);

    // The most a stretch can add to a mesh: its far row, and its near one where it is the mesh's
    // first or the road changes kind there; and at an end a cap's corners or, past a run's end, four rows of fall.
    private const int RowVertices = 11, RowIndices = 36, EndVertices = 4 * RowVertices, EndIndices = 2 * RowIndices;

    public static List<RoadMeshData> Mesh(RoadRibbon ribbon, Layout layout, Fit? fit = null)
    {
        Station[] stations = layout.Stations;
        int count = layout.Stretches.Length, chunks = Math.Max(1, (int)Math.Round(ribbon.LengthM / ChunkM));
        Dictionary<(int, bool), Row> rows = [];

        Row RowAt(int station, bool deck)
        {
            if (!rows.TryGetValue((station, deck), out Row? row))
            {
                rows[(station, deck)] = row = deck ? DeckRow(ribbon, stations[station].At) : GroundRow(ribbon, stations[station].At);
            }
            return row;
        }

        // A stretch is a deck or it is not by where its middle is, and belongs to the mesh its middle is in.
        bool[] deck = new bool[count];
        int[] chunkOf = new int[count];
        for (int k = 0; k < count; k++)
        {
            (int from, int to) = layout.Stretches[k];
            double end = to == 0 && k == count - 1 ? ribbon.LengthM : stations[to].S;
            double middle = 0.5 * (stations[from].S + end);
            deck[k] = ribbon.DeckOver(Math.Min((int)(middle / ribbon.SideStepM), ribbon.SideStretches - 1));
            chunkOf[k] = Math.Clamp((int)(middle / ribbon.LengthM * chunks), 0, chunks - 1);
        }

        if (fit is { } most)
        {
            int vertices = 0, indices = 0;
            double startS = 0.0, panelFrom = double.NaN;
            chunks = 1;
            for (int k = 0; k < count; k++)
            {
                (int from, int to) = layout.Stretches[k];
                double end = to == 0 && k == count - 1 ? ribbon.LengthM : stations[to].S;
                int ends = (k == 0 && !ribbon.Closed) || deck[(k + count - 1) % count] != deck[k] ? 1 : 0;
                if ((k == count - 1 && !ribbon.Closed) || deck[(k + 1) % count] != deck[k]) ends++;

                // A barrier's panel ends where it is long enough, where the deck does, and where the mesh does: room is kept
                // for that last one, which is only known of once the mesh is full.
                if (deck[k] && double.IsNaN(panelFrom)) panelFrom = stations[from].S;
                bool panel = deck[k] && (end - panelFrom >= BarrierStepM || k == count - 1 || !deck[k + 1]);
                int kept = deck[k] ? BarrierVertices : 0, keptIndices = deck[k] ? BarrierIndices : 0;
                int v = RowVertices + (ends * EndVertices) + (panel ? BarrierVertices : 0), i = RowIndices + (ends * EndIndices) + (panel ? BarrierIndices : 0);
                bool joined = vertices > 0 && deck[k - 1] == deck[k];

                if (vertices > 0 && (vertices + v + kept + (joined ? 0 : RowVertices) > most.Vertices || indices + i + keptIndices > most.Indices || end - startS > most.LengthM))
                {
                    chunks++;
                    (vertices, indices, startS, joined) = (0, 0, stations[from].S, false);
                    if (deck[k]) panelFrom = stations[from].S;
                    panel = deck[k] && (end - panelFrom >= BarrierStepM || k == count - 1 || !deck[k + 1]);
                    v = RowVertices + (ends * EndVertices) + (panel ? BarrierVertices : 0);
                    i = RowIndices + (ends * EndIndices) + (panel ? BarrierIndices : 0);
                }
                if (panel || !deck[k]) panelFrom = double.NaN;
                vertices += v + (joined ? 0 : RowVertices);
                indices += i;
                chunkOf[k] = chunks - 1;
            }
        }

        List<RoadMeshData> meshes = [];
        for (int c = 0; c < chunks; c++)
        {
            Chunk chunk = new();
            int first = -1, last = -1;
            RoadRibbon.Section? panelFrom = null;
            for (int k = 0; k < count; k++)
            {
                if (chunkOf[k] != c) continue;
                if (first < 0) first = k;
                last = k;

                (int from, int to) = layout.Stretches[k];
                bool bend = Math.Max(Math.Abs(stations[from].At.Curvature), Math.Abs(stations[to].At.Curvature)) > 1.0 / KerbRadiusM;
                chunk.Between(RowAt(from, deck[k]), RowAt(to, deck[k]), deck[k] ? DeckStrips : bend ? KerbedStrips : GroundStrips);
                if (deck[k])
                {
                    // A barrier in panels longer than the rows are apart: it is straight where the road's edge bends a hand's width.
                    panelFrom ??= stations[from].At;
                    double upTo = to == 0 && k == count - 1 ? ribbon.LengthM : stations[to].S;
                    if (upTo - panelFrom.Value.S >= BarrierStepM || k == count - 1 || !deck[k + 1] || chunkOf[k + 1] != c)
                    {
                        Barrier(chunk, ribbon, panelFrom.Value, stations[to].At);
                        panelFrom = null;
                    }
                }

                // What is before this stretch, and what is after it: nothing at an open run's end, and the other kind at a change.
                // Nor is anything put at a junction's mouth, where the junction's own triangles carry on from the row.
                bool starts = k == 0 && !ribbon.Closed, ends = k == count - 1 && !ribbon.Closed;
                if (starts ? ribbon.StartJunction is null : deck[(k + count - 1) % count] != deck[k]) End(chunk, ribbon, stations[from].At, deck[k], -1.0, starts);
                if (ends ? ribbon.EndJunction is null : deck[(k + 1) % count] != deck[k]) End(chunk, ribbon, stations[to].At, deck[k], 1.0, ends);
            }
            if (first < 0) continue;

            Row start = RowAt(layout.Stretches[first].From, deck[first]), end = RowAt(layout.Stretches[last].To, deck[last]);
            double fromS = stations[layout.Stretches[first].From].S;
            double toS = layout.Stretches[last].To == 0 && last == count - 1 ? ribbon.LengthM : stations[layout.Stretches[last].To].S;
            RoadRibbon.Section middle = ribbon.At(0.5 * (fromS + toS));
            meshes.Add(Narrow(chunk, ribbon.Point(middle, 0.0, middle.Height), fromS, toS, Asphalt(start, deck[first]), Asphalt(end, deck[last])));
        }
        return meshes;
    }

    // The row of vertices a run's mesh has at one of its ends, to the last bit: what a junction's edge takes at the mouth.
    private static Row EndRow(RoadRibbon ribbon, bool atStart, bool deck)
    {
        RoadRibbon.Section at = atStart ? ribbon.At(0, 0.0) : ribbon.At(ribbon.Line.Count - 1, ribbon.LengthM);
        return deck ? DeckRow(ribbon, at) : GroundRow(ribbon, at);
    }

    // Where in a run's last row the vertices of one side are, the side being the left of someone leaving
    // the junction or their right: a run that ends at the mouth has its own left on the other side.
    // On the ground, from the asphalt's edge out to the bank's foot; round a deck, the top and bottom of
    // the side, and the underside's corner.
    private static int[] Side(RoadJunction.Arm arm, bool left, bool deck) =>
        left == arm.AtStart ? (deck ? [8, 7, 6] : [3, 2, 1, 0]) : (deck ? [3, 4, 5] : [7, 8, 9, 10]);

    /// <summary>
    /// A junction as one mesh: its asphalt, a fan about its point or whatever triangles its polygon
    /// was cut into, and round it between the mouths a verge and an embankment, or a deck's sides and
    /// underside. At each mouth its vertices are the ones the arm's run ends on.
    /// </summary>
    public static RoadMeshData Mesh(RoadJunction junction)
    {
        Chunk chunk = new();
        Plan[] edge = junction.Boundary;
        int n = edge.Length;
        bool deck = junction.Deck;

        double3[] top = new double3[n];
        for (int i = 0; i < n; i++) top[i] = junction.Point(edge[i]);
        Dictionary<int, (Row Row, int[] Side)> corners = [];
        foreach (RoadJunction.Arm arm in junction.Arms)
        {
            if (arm.Ribbon is not { } ribbon) continue;
            Row row = EndRow(ribbon, arm.AtStart, deck);
            double3[] asphalt = Asphalt(row, deck);
            (top[arm.Mouth.Left], top[arm.Mouth.Centre], top[arm.Mouth.Right]) = arm.AtStart ? (asphalt[0], asphalt[1], asphalt[2]) : (asphalt[2], asphalt[1], asphalt[0]);
            corners[arm.Mouth.Left] = (row, Side(arm, true, deck));
            corners[arm.Mouth.Right] = (row, Side(arm, false, deck));
        }

        int first = chunk.At.Count;
        for (int i = 0; i < n; i++) chunk.Add(top[i], junction.Normal(edge[i]), Tile(edge[i]));
        chunk.Add(junction.Point(junction.Hub), junction.Normal(junction.Hub), Tile(junction.Hub));
        for (int f = 0; f + 2 < junction.Faces.Length; f += 3)
        {
            chunk.Triangle(first + junction.Faces[f], first + junction.Faces[f + 1], first + junction.Faces[f + 2], Kind.Asphalt);
        }

        if (deck)
        {
            Under(chunk, junction, corners);
        }

        int[][] rows = new int[junction.Spokes.Length][];
        for (int k = 0; k < rows.Length; k++)
        {
            rows[k] = deck ? Wall(chunk, junction, junction.Spokes[k], top, corners) : Earth(chunk, junction, k, top, corners);
        }
        foreach ((int from, int to) in junction.Strips)
        {
            for (int k = 0; k + 1 < rows[to].Length; k += 2)
            {
                chunk.Triangle(rows[from][k], rows[from][k + 1], rows[to][k + 1], deck ? Kind.Deck : Kind.Earth);
                chunk.Triangle(rows[from][k], rows[to][k + 1], rows[to][k], deck ? Kind.Deck : Kind.Earth);
            }
        }
        return Narrow(chunk, junction.Point(junction.At), 0.0, 0.0, [], []);
    }

    /// <summary>
    /// Junctions' meshes put together, as many to a mesh as <paramref name="fit"/> holds and no two in
    /// one that are further apart than it allows: a junction is a few hundred vertices, and a place in
    /// the pool for each would leave a circuit of many junctions with none for its roads.
    /// </summary>
    /// <returns>Each mesh, with the junctions' points it is of.</returns>
    public static List<(int[] Nodes, RoadMeshData Mesh)> Gather(IReadOnlyList<(RoadJunction Junction, RoadMeshData Mesh)> junctions, Fit fit)
    {
        List<(int[], RoadMeshData)> gathered = [];
        bool[] taken = new bool[junctions.Count];
        for (int i = 0; i < junctions.Count; i++)
        {
            if (taken[i]) continue;
            List<int> group = [i];
            int vertices = junctions[i].Mesh.Positions.Length, indices = junctions[i].Mesh.Indices.Length;
            for (int k = i + 1; k < junctions.Count; k++)
            {
                RoadMeshData more = junctions[k].Mesh;
                if (taken[k] || Vec.Len(more.Origin - junctions[i].Mesh.Origin) > fit.LengthM) continue;
                if (vertices + more.Positions.Length > fit.Vertices || indices + more.Indices.Length > fit.Indices) continue;
                taken[k] = true;
                group.Add(k);
                (vertices, indices) = (vertices + more.Positions.Length, indices + more.Indices.Length);
            }
            gathered.Add(([.. group.Select(k => junctions[k].Junction.Node)], group.Count == 1 ? junctions[i].Mesh : Merged([.. group.Select(k => junctions[k].Mesh)])));
        }
        return gathered;
    }

    // Several meshes as one, about the middle of their origins, the triangles of each kind together.
    private static RoadMeshData Merged(RoadMeshData[] meshes)
    {
        double3 origin = Vec.Zero;
        foreach (RoadMeshData mesh in meshes) origin += mesh.Origin / meshes.Length;

        List<float3> positions = [], normals = [];
        List<float2> uvs = [];
        List<double3> places = [];
        List<int>[] triangles = [[], [], []];
        double radius = 0.0;
        int folded = 0;
        foreach (RoadMeshData mesh in meshes)
        {
            int first = places.Count;
            foreach (double3 place in mesh.Places)
            {
                double3 from = place - origin;
                positions.Add(new float3((float)from.X, (float)from.Y, (float)from.Z));
                radius = Math.Max(radius, Vec.Len(from));
            }
            places.AddRange(mesh.Places);
            normals.AddRange(mesh.Normals);
            uvs.AddRange(mesh.Uvs);
            folded += mesh.Folded;
            for (int t = 0; t < mesh.Indices.Length; t++)
            {
                int kind = t < mesh.AsphaltIndices ? 0 : t < mesh.AsphaltIndices + mesh.EarthIndices ? 1 : 2;
                triangles[kind].Add(first + mesh.Indices[t]);
            }
        }
        return new RoadMeshData(origin, [.. positions], [.. normals], [.. uvs], [.. triangles[0], .. triangles[1], .. triangles[2]],
                                triangles[0].Count, triangles[1].Count, triangles[2].Count, radius, 0.0, 0.0, [], [], folded, [.. places]);
    }

    // Out from the junction's edge along one of its spokes: the verge's two edges and the bank's top and foot.
    private static int[] Earth(Chunk chunk, RoadJunction junction, int spoke, double3[] top, Dictionary<int, (Row Row, int[] Side)> corners)
    {
        RoadJunction.Spoke along = junction.Spokes[spoke];
        if (along.Corner && corners.TryGetValue(along.At, out (Row Row, int[] Side) corner))
        {
            return [.. corner.Side.Select(v => chunk.Add(corner.Row.At[v], corner.Row.Normal[v], corner.Row.Uv[v]))];
        }

        Plan place = junction.Boundary[along.At], outward = along.Outward;
        double reach = junction.OutOf(spoke), verge = Math.Min(reach, RoadRibbon.VergeM);
        Plan atVerge = place + (outward * verge), atFoot = place + (outward * reach);
        double3 onVerge = junction.Facing(place, junction.Gradient - (outward * RoadRibbon.VergeSlope));
        double3 onBank = junction.Facing(place, junction.Gradient - (outward * RoadRibbon.BankSlope));
        double3 edge = junction.Chart.Dir(atVerge) * (junction.Chart.RadiusM + junction.HeightAt(place) - RoadRibbon.Drop(verge));
        double3 foot = junction.Chart.Dir(atFoot) * (junction.Chart.RadiusM + junction.HeightAt(place) - RoadRibbon.Drop(reach));
        return [chunk.Add(top[along.At], onVerge, Tile(place)), chunk.Add(edge, onVerge, Tile(atVerge)), chunk.Add(edge, onBank, Tile(atVerge)), chunk.Add(foot, onBank, Tile(atFoot))];
    }

    // Down a deck's side at one of a junction's spokes: its top and its bottom, which at a mouth's corner are where the run's are.
    private static int[] Wall(Chunk chunk, RoadJunction junction, RoadJunction.Spoke along, double3[] top, Dictionary<int, (Row Row, int[] Side)> corners)
    {
        bool mouth = corners.TryGetValue(along.At, out (Row Row, int[] Side) corner);
        if (mouth && along.Corner)
        {
            return [chunk.Add(corner.Row.At[corner.Side[0]], corner.Row.Normal[corner.Side[0]], corner.Row.Uv[corner.Side[0]]),
                    chunk.Add(corner.Row.At[corner.Side[1]], corner.Row.Normal[corner.Side[1]], corner.Row.Uv[corner.Side[1]])];
        }

        Plan place = junction.Boundary[along.At], outward = along.Outward;
        junction.Chart.Compass(place, out _, out double3 east, out double3 north);
        double3 facing = (east * outward.E) + (north * outward.N);
        double height = junction.HeightAt(place);
        float across = (float)((Math.Abs(outward.N) >= Math.Abs(outward.E) ? place.E : place.N) / TileM);
        return [chunk.Add(top[along.At], facing, new float2(across, (float)(height / TileM))),
                chunk.Add(mouth ? corner.Row.At[corner.Side[1]] : junction.Point(place, RoadRibbon.DeckThickM), facing, new float2(across, (float)((height - RoadRibbon.DeckThickM) / TileM)))];
    }

    // A deck's underside: the asphalt's own triangles the other way up, a deck's thickness down. A run's
    // deck has no vertex under its centre line, so where the asphalt is a fan the underside's goes from
    // corner to corner of each mouth.
    private static void Under(Chunk chunk, RoadJunction junction, Dictionary<int, (Row Row, int[] Side)> corners)
    {
        Plan[] edge = junction.Boundary;
        int n = edge.Length;
        int[] at = new int[n + 1];
        for (int i = 0; i <= n; i++)
        {
            Plan place = i < n ? edge[i] : junction.Hub;
            double3 down = -junction.Normal(place);
            at[i] = i < n && corners.TryGetValue(i, out (Row Row, int[] Side) corner)
                ? chunk.Add(corner.Row.At[corner.Side[2]], down, Tile(place))
                : chunk.Add(junction.Point(place, RoadRibbon.DeckThickM), down, Tile(place));
        }

        bool fan = junction.Faces.Length == 3 * n && junction.Faces[0] == n;
        if (!fan)
        {
            for (int f = 0; f + 2 < junction.Faces.Length; f += 3) chunk.Triangle(at[junction.Faces[f]], at[junction.Faces[f + 2]], at[junction.Faces[f + 1]], Kind.Deck);
            return;
        }

        HashSet<int> centres = [.. junction.Arms.Select(a => a.Mouth.Centre)];
        int before = n - 1;
        while (centres.Contains(before)) before--;
        for (int i = 0; i < n; i++)
        {
            if (centres.Contains(i)) continue;
            chunk.Triangle(at[n], at[i], at[before], Kind.Deck);
            before = i;
        }
    }

    private static double3[] Asphalt(Row row, bool deck) => deck ? [row.At[0], row.At[1], row.At[2]] : [row.At[4], row.At[5], row.At[6]];

    // What closes a stretch where nothing of its own kind carries on: a deck's open end, the bank
    // under a road where a deck takes over, and past the end of a run on the ground the fall ahead of it.
    private static void End(Chunk chunk, RoadRibbon ribbon, in RoadRibbon.Section at, bool deck, double ahead, bool runEnds)
    {
        ribbon.Chart.Compass(at.At, out _, out double3 east, out double3 north);
        double3 facing = ((east * at.Heading.E) + (north * at.Heading.N)) * ahead;

        if (deck)
        {
            Row row = DeckRow(ribbon, at);
            double half = at.HalfWidth, top = at.Height, under = top - RoadRibbon.DeckThickM;
            chunk.Cap([row.At[1], row.At[0], row.At[7], row.At[4], row.At[2]],
                      [Flat(0.0, top), Flat(half, top), Flat(half, under), Flat(-half, under), Flat(-half, top)], facing, Kind.Deck);
            return;
        }

        if (!runEnds)
        {
            Row ground = GroundRow(ribbon, at);
            double half = at.HalfWidth, vergeLeft = Math.Min(at.ToeLeft, RoadRibbon.VergeM), vergeRight = Math.Min(at.ToeRight, RoadRibbon.VergeM);

            // From the left foot over the road to the right one. A fan from the centre will not do: where
            // the road leans, its low edge is a hollow in this outline and the fan's triangle bridges it,
            // a wedge of wall a hand above the asphalt at the edge a car cuts closest to.
            double[] across = [half + at.ToeLeft, half + vergeLeft, half, 0.0, -half, -half - vergeRight, -half - at.ToeRight];
            RoadRibbon.Section here = at;
            chunk.Curtain([ground.At[0], ground.At[2], ground.At[4], ground.At[5], ground.At[6], ground.At[8], ground.At[10]],
                          [.. across.Select(d => Flat(d, Lateral(here, d)))], facing, Kind.Earth);
            return;
        }

        // The verge's fall for as far as a verge goes, and the bank's from there until both edges are buried.
        double reach = Math.Max(at.ToeLeft, at.ToeRight), verge = Math.Min(reach, RoadRibbon.VergeM);
        if (reach <= 0.0) return;
        Fall(chunk, GroundRow(ribbon, at, 0.0, RoadRibbon.VergeSlope), GroundRow(ribbon, at, verge, RoadRibbon.VergeSlope), ahead);
        if (reach > verge) Fall(chunk, GroundRow(ribbon, at, verge, RoadRibbon.BankSlope), GroundRow(ribbon, at, reach, RoadRibbon.BankSlope), ahead);
    }

    private static readonly (int Left, Kind Kind)[] FallStrips = [.. GroundStrips.Select(s => (s.Left, Kind.Earth))];

    private static void Fall(Chunk chunk, Row near, Row far, double ahead)
    {
        if (ahead > 0.0) chunk.Between(near, far, FallStrips);
        else chunk.Between(far, near, FallStrips);
    }

    private static float2 Flat(double across, double height) => new((float)(across / TileM), (float)(height / TileM));

    private static RoadMeshData Narrow(Chunk chunk, double3 origin, double fromS, double toS, double3[] start, double3[] end)
    {
        int n = chunk.At.Count;
        float3[] positions = new float3[n], normals = new float3[n];
        double radius = 0.0;
        for (int i = 0; i < n; i++)
        {
            double3 from = chunk.At[i] - origin, normal = chunk.Normal[i];
            positions[i] = new float3((float)from.X, (float)from.Y, (float)from.Z);
            normals[i] = new float3((float)normal.X, (float)normal.Y, (float)normal.Z);
            radius = Math.Max(radius, Vec.Len(from));
        }
        return new RoadMeshData(origin, positions, normals, [.. chunk.Uv],
                                [.. chunk.Triangles[0], .. chunk.Triangles[1], .. chunk.Triangles[2], .. chunk.Triangles[3]],
                                chunk.Triangles[0].Count, chunk.Triangles[1].Count, chunk.Triangles[2].Count,
                                radius, fromS, toS, start, end, chunk.Folded, [.. chunk.At], chunk.Triangles[3].Count);
    }
}
