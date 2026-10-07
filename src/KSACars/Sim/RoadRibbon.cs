using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// A run of roads as one surface: where it is a distance along its centre line and a distance to the
/// left of it, which way it faces there, and for any place on the chart how far along and across it
/// that is. What is drawn, what a wheel is over and what a collider is made from are all this.
///
/// <para>Across, the asphalt leans by the bank and is otherwise straight. Past its edge a verge falls
/// at <see cref="VergeSlope"/> for <see cref="VergeM"/>, and an embankment at <see cref="BankSlope"/>
/// from there to <see cref="BuriedM"/> under the ground. Where an edge is more than
/// <see cref="DeckOverM"/> above the ground the road is a deck instead, with nothing past its edge.
/// Past an end that is not a deck the whole section carries on ahead, sunk by the same fall.</para>
///
/// <para>Lengths are the chart's, which a road <c>r</c> metres from the chart's middle and <c>h</c>
/// above the body's mean radius has too long by <c>(r / 2R)^2 - h / R</c> of themselves.</para>
/// </summary>
internal sealed class RoadRibbon
{
    public const double VergeSlope = 1.0 / 15.0, VergeM = 1.5, BankSlope = 0.5, BuriedM = 0.3;

    /// <summary>An edge higher than this over the ground makes a deck, this thick; and a stretch between two decks shorter than the hold is a deck too.</summary>
    public const double DeckOverM = 4.0, DeckThickM = 0.5, DeckHoldM = 20.0;

    /// <summary>The furthest out from an edge an embankment is followed: past it the ground has fallen away, and the bank stops in the air.</summary>
    public const double MostToeM = 24.0;

    /// <summary>
    /// A bend's radius is to be at least this many times the half width and the verge. At once that,
    /// the inside of the verge has no length left and the surface folds over itself.
    /// </summary>
    public const double RadiusFloor = 1.25;

    /// <summary>How far apart the places along a ribbon are that a lookup starts from.</summary>
    public const double LookupM = 2.0;

    /// <summary>One of the circuit's roads in the run: its two points in the order the run passes them, and where along the run each is.</summary>
    public readonly record struct Span(int From, int To, double FromS, double ToS);

    /// <summary>Everything about the road at one distance along it.</summary>
    /// <param name="Height">The centre line's, over the body's mean radius.</param>
    /// <param name="Slope">Its climb for each metre along, and <paramref name="Bend"/> how fast that changes.</param>
    /// <param name="BankTan">The tangent of the lean, the left edge up positive, and <paramref name="BankRate"/> how fast it changes.</param>
    /// <param name="ToeLeft">How far out from the left edge the surface ends.</param>
    public readonly record struct Section(double S, Plan At, Plan Heading, double Curvature, double Height, double Slope, double Bend,
                                          double BankTan, double BankRate, double HalfWidth, double HalfRate,
                                          bool Deck, double ToeLeft, double ToeRight)
    {
        public Plan Left => Heading.Left();
    }

    public RoadChart Chart { get; }
    public RoadLine Line { get; }
    public RoadProfile Profile { get; }
    public IReadOnlyList<Span> Spans { get; }
    public double LengthM => Line.LengthM;
    public bool Closed => Line.Closed;

    /// <summary>The most any part of the ribbon reaches out from its centre line.</summary>
    public double ReachM { get; }

    /// <summary>
    /// Whether the surface was laid as a road and so has a facing of its own. One made from a line of
    /// points given by hand is only as smooth as that line, which may have a step in it.
    /// </summary>
    public bool Laid { get; }

    private readonly double _sideStepM;
    private readonly double[] _toeLeft, _toeRight;
    private readonly bool[] _deck;

    private readonly Plan[] _lookup;
    private readonly double _lookupStepM;

    /// <summary>How far apart the places are that the sides were worked out at: a deck starts and ends on one.</summary>
    public double SideStepM => _sideStepM;

    private RoadRibbon(RoadChart chart, RoadLine line, RoadProfile profile, IReadOnlyList<Span> spans,
                       int steps, Func<double, double, double> terrainAt, double[]? leftGround, double[]? rightGround)
    {
        (Chart, Line, Profile, Spans) = (chart, line, profile, spans);
        Laid = leftGround is not null;

        _sideStepM = line.LengthM / steps;
        _toeLeft = new double[steps + 1];
        _toeRight = new double[steps + 1];
        bool[] high = new bool[steps + 1];
        double widest = 0.0, furthest = 0.0;
        for (int i = 0; i <= steps; i++)
        {
            double s = i * _sideStepM;
            profile.Height(s, out double height, out _, out _);
            profile.Bank(s, out double bank, out _);
            profile.HalfWidth(s, out double half, out _);
            double left = height + (half * bank), right = height - (half * bank);
            double underLeft = leftGround?[i] ?? terrainAt(s, half), underRight = rightGround?[i] ?? terrainAt(s, -half);

            high[i] = Math.Max(left - underLeft, right - underRight) > DeckOverM;
            _toeLeft[i] = Toe(left, underLeft, out_ => terrainAt(s, half + out_));
            _toeRight[i] = Toe(right, underRight, out_ => terrainAt(s, -half - out_));
            widest = Math.Max(widest, half);
            furthest = Math.Max(furthest, Math.Max(_toeLeft[i], _toeRight[i]));
        }
        ReachM = widest + furthest;
        _deck = Decks(high, _sideStepM, line.Closed);

        int places = Math.Max(1, (int)Math.Ceiling(line.LengthM / LookupM));
        _lookupStepM = line.LengthM / places;
        _lookup = new Plan[line.Closed ? places : places + 1];
        for (int i = 0; i < _lookup.Length; i++) _lookup[i] = line.At(i * _lookupStepM).At;
    }

    /// <summary>
    /// A run laid on the ground: the highest ground across it is read at every <paramref name="stepM"/>,
    /// smoothed along it over <paramref name="smoothM"/>, and the road put on that.
    /// </summary>
    /// <param name="profile">The road without its ground: its heights are above whatever ground it is given here.</param>
    /// <param name="terrainAt">The ground's height over the body's mean radius at a place on the chart.</param>
    public static RoadRibbon Lay(RoadChart chart, RoadLine line, RoadProfile profile, IReadOnlyList<Span> spans,
                                 Func<Plan, double> terrainAt, double stepM, double smoothM)
    {
        double Terrain(double s, double d)
        {
            RoadLine.Point on = line.At(s);
            return terrainAt(on.At + (on.Heading.Left() * d));
        }

        int steps = Math.Max(1, (int)Math.Ceiling(line.LengthM / Math.Max(stepM, 0.1)));
        double each = line.LengthM / steps;
        double[] highest = new double[line.Closed ? steps : steps + 1], left = new double[steps + 1], right = new double[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            double s = i * each;
            profile.Bank(s, out double bank, out _);
            profile.HalfWidth(s, out double half, out _);

            // Against the leaning section: ground under the raised edge may stand that much higher before it is through the road.
            left[i] = Terrain(s, half);
            right[i] = Terrain(s, -half);
            double most = Math.Max(left[i] - (half * bank), right[i] + (half * bank));
            most = Math.Max(most, Terrain(s, 0.0));
            most = Math.Max(most, Math.Max(Terrain(s, 0.5 * half) - (0.5 * half * bank), Terrain(s, -0.5 * half) + (0.5 * half * bank)));
            if (i < highest.Length) highest[i] = most;
            else highest[0] = Math.Max(highest[0], most);
        }
        return new RoadRibbon(chart, line, profile.Over(RoadGround.Smooth(highest, each, smoothM, line.Closed)), spans, steps, Terrain, left, right);
    }

    /// <summary>
    /// A surface whose centre line's height is already known, with the ground taken to be level
    /// across and <paramref name="aboveGround"/> under it a distance along.
    /// </summary>
    public static RoadRibbon Over(RoadChart chart, RoadLine line, RoadProfile profile, Func<double, double> aboveGround)
    {
        double Terrain(double s, double d)
        {
            profile.Height(s, out double height, out _, out _);
            return height - aboveGround(s);
        }
        return new RoadRibbon(chart, line, profile, [], Math.Max(1, (int)Math.Ceiling(line.LengthM / LookupM)), Terrain, null, null);
    }

    // How far out from an edge the verge and the embankment go before they are buried.
    private static double Toe(double edge, double under, Func<double, double> terrainOut)
    {
        double Gap(double out_) => edge - Drop(out_) - (terrainOut(out_) - BuriedM);

        double before = edge - under + BuriedM;
        if (!(before > 0.0)) return 0.0;
        double gap = Gap(VergeM), at = VergeM;
        if (gap <= 0.0) return VergeM * before / (before - gap);

        // The bank closes on level ground by its slope each metre, and faster on ground that rises to
        // meet it, so less than the whole gap is stepped at a time.
        while (at < MostToeM)
        {
            double next = Math.Min(at + Math.Max(0.5, 1.5 * gap), MostToeM);
            double then = Gap(next);
            if (then <= 0.0) return at + ((next - at) * gap / (gap - then));
            (at, gap) = (next, then);
        }
        return MostToeM;
    }

    /// <summary>How far the surface has fallen from the road's edge a distance out from it.</summary>
    public static double Drop(double outM) =>
        outM <= VergeM ? outM * VergeSlope : (VergeM * VergeSlope) + ((outM - VergeM) * BankSlope);

    // A stretch is a deck where either end of it is high, and so is a short one between two decks:
    // a road that dips under the height for a few metres would otherwise change what it is twice.
    private static bool[] Decks(bool[] high, double stepM, bool closed)
    {
        int n = high.Length - 1;
        bool[] deck = new bool[n];
        for (int i = 0; i < n; i++) deck[i] = high[i] || high[i + 1];

        int first = Array.IndexOf(deck, true);
        if (first < 0) return deck;
        int hold = (int)Math.Floor(DeckHoldM / stepM);

        // From a deck onwards, and on a closed run round to it again, so every gap met has a deck before it.
        int last = closed ? first + n : n;
        for (int i = first; i < last;)
        {
            if (deck[i % n])
            {
                i++;
                continue;
            }
            int end = i;
            while (end < last && !deck[end % n]) end++;
            if (end - i <= hold && (closed || end < n))
            {
                for (int j = i; j < end; j++) deck[j % n] = true;
            }
            i = end;
        }
        return deck;
    }

    /// <summary>Whether the road is a deck over the stretch between two of the places its sides were worked out at.</summary>
    public bool DeckOver(int stretch) => _deck[Math.Clamp(stretch, 0, _deck.Length - 1)];

    public int SideStretches => _deck.Length;

    /// <summary>The road <paramref name="s"/> metres along the run.</summary>
    public Section At(double s)
    {
        s = Line.Wrap(s);
        return At(Line.ArcAt(s), s);
    }

    /// <summary>The same on a curve of the centre line that is named, which at a kink says which side of it is meant.</summary>
    public Section At(int arc, double s)
    {
        RoadLine.Point on = Line.At(arc, s);
        Profile.Height(s, out double height, out double slope, out double bend);
        Profile.Bank(s, out double bank, out double bankRate);
        Profile.HalfWidth(s, out double half, out double halfRate);

        double along = s / _sideStepM;
        int i = Math.Clamp((int)Math.Floor(along), 0, _deck.Length - 1);
        double t = Math.Clamp(along - i, 0.0, 1.0);
        return new Section(s, on.At, on.Heading, on.Curvature, height, slope, bend, bank, bankRate, half, halfRate, _deck[i],
                           _toeLeft[i] + ((_toeLeft[i + 1] - _toeLeft[i]) * t), _toeRight[i] + ((_toeRight[i + 1] - _toeRight[i]) * t));
    }

    /// <summary>
    /// The surface's height over the body's mean radius <paramref name="d"/> metres to the left of the
    /// centre line, and <paramref name="beyondM"/> past the run's end if it is; false where there is
    /// no surface there.
    /// </summary>
    /// <param name="outM">How far out past the asphalt that is: nothing on it.</param>
    /// <param name="alongRise">The surface's rise for each metre along the run, at that distance across.</param>
    /// <param name="acrossRise">Its rise for each metre to the left.</param>
    public bool Surface(in Section at, double d, double beyondM, out double height, out double outM, out double alongRise, out double acrossRise)
    {
        double side = d < 0.0 ? -1.0 : 1.0, beside = Math.Abs(d) - at.HalfWidth;
        outM = Math.Max(Math.Max(beside, beyondM), 0.0);
        if (!(beside > 0.0))
        {
            height = at.Height + (d * at.BankTan);
            alongRise = at.Slope + (d * at.BankRate);
            acrossRise = at.BankTan;
        }
        else
        {
            height = alongRise = acrossRise = 0.0;
            if (at.Deck || beside > (side > 0.0 ? at.ToeLeft : at.ToeRight)) return false;
            height = at.Height + (side * at.HalfWidth * at.BankTan) - Drop(beside);
            SideRises(at, side, Fall(beside), out alongRise, out acrossRise);
        }
        if (!(beyondM > 0.0)) return true;

        // Past an end the whole section carries on ahead as it was there, sunk by the same fall as
        // goes out from an edge, until both its edges are buried.
        if (at.Deck || beyondM > Math.Max(at.ToeLeft, at.ToeRight)) return false;
        height -= Drop(beyondM);
        alongRise = at.S > 0.5 * LengthM ? -Fall(beyondM) : Fall(beyondM);
        return true;
    }

    /// <summary>How steeply the surface falls a distance out from the road's edge: the verge's slope, then the bank's.</summary>
    public static double Fall(double outM) => outM <= VergeM ? VergeSlope : BankSlope;

    /// <summary>
    /// The two rises of the verge or the bank on one side, <paramref name="side"/> being 1 for the
    /// left and -1 for the right: it falls away from the edge at <paramref name="fall"/>, and along
    /// the road it rises as the edge does, which moves out and up as the road widens and leans.
    /// </summary>
    public static void SideRises(in Section at, double side, double fall, out double alongRise, out double acrossRise)
    {
        alongRise = at.Slope + (side * ((at.HalfRate * at.BankTan) + (at.HalfWidth * at.BankRate))) + (at.HalfRate * fall);
        acrossRise = -side * fall;
    }

    /// <summary>A place on the surface, from the body's centre.</summary>
    public double3 Point(in Section at, double d, double height) => Chart.Dir(at.At + (at.Left * d)) * (Chart.RadiusM + height);

    /// <summary>
    /// The way the surface faces at a place on it, up and out of the road, from the two rises
    /// <see cref="Surface"/> gives there.
    /// </summary>
    public double3 Normal(in Section at, double d, double height, double alongRise, double acrossRise)
    {
        Plan place = at.At + (at.Left * d);
        Chart.Compass(place, out double3 up, out double3 east, out double3 north);
        double3 along = (east * at.Heading.E) + (north * at.Heading.N), left = (east * at.Left.E) + (north * at.Left.N);

        // On the inside of a bend a metre along the centre line is less than a metre of surface, by
        // this; held above nothing where the bend is tighter than the road is wide and the surface folds.
        double squeeze = Math.Max(1.0 - (d * at.Curvature), 0.05);
        double metres = (Chart.RadiusM + height) / (Chart.RadiusM * Chart.Scale(place));
        return Vec.Unit((up * (squeeze * metres)) - (along * alongRise) - (left * (squeeze * acrossRise)));
    }

    /// <summary>
    /// Where the run turns tighter than <see cref="RadiusFloor"/> allows the road on it, each stretch
    /// with its least radius and the one it needs. Which road a stretch is on is <see cref="Spans"/>'s to say.
    /// </summary>
    public List<RoadLine.Tight> TooTight() => Line.TooTight(s =>
    {
        Profile.HalfWidth(s, out double half, out _);
        return RadiusFloor * (half + VergeM);
    });

    public int LookupCount => _lookup.Length;

    public Plan LookupAt(int index) => _lookup[index];

    /// <summary>
    /// Where a place on the chart is along and across the run, looked for either side of lookup place
    /// <paramref name="index"/>: the distance along at which the line from the centre line to it is
    /// square to the road. False where there is no such distance there, which is another lookup
    /// place's to answer.
    /// </summary>
    /// <param name="d">How far to the left of the centre line; round the outside of a kink, how far from it.</param>
    /// <param name="beyondM">How far past the end of an open run, or nothing.</param>
    public bool Locate(Plan place, int index, out Section at, out double d, out double beyondM)
    {
        double middle = index * _lookupStepM;
        double low = middle - _lookupStepM, high = middle + _lookupStepM;
        if (!Closed) (low, high) = (Math.Max(low, 0.0), Math.Min(high, LengthM));

        double Ahead(double s, out RoadLine.Point on)
        {
            on = Line.At(s);
            return Plan.Dot(place - on.At, on.Heading);
        }

        // Newton on how far ahead of the foot the place is, kept between two distances along that the
        // foot is known to lie between. At a kink that jumps from ahead to behind, and halving finds
        // the kink. Where it only ever says ahead, or only behind, the foot is not here: near the
        // middle of a tight bend every place along it is nearly square to the point, and the nearest
        // lookup place need not have the foot beside it.
        (at, d, beyondM) = (default, 0.0, 0.0);
        double s = middle;
        bool after = false, before = false;
        for (int i = 0; i < 60; i++)
        {
            double ahead = Ahead(s, out RoadLine.Point on);
            if (Math.Abs(ahead) < 1e-8)
            {
                after = before = true;
                break;
            }
            if (ahead > 0.0) (low, after) = (s, true);
            else (high, before) = (s, true);
            if (high - low < 1e-9) break;
            double squeeze = 1.0 - (on.Curvature * Plan.Dot(place - on.At, on.Heading.Left()));
            double next = squeeze > 0.05 ? s + (ahead / squeeze) : double.NaN;
            s = next > low && next < high ? next : 0.5 * (low + high);
        }
        if (!before)
        {
            if (Closed || high < LengthM) return false;
            s = LengthM;
        }
        if (!after)
        {
            if (Closed || low > 0.0) return false;
            s = 0.0;
        }

        at = At(s);
        Plan off = place - at.At;
        double along = Plan.Dot(off, at.Heading);
        d = Plan.Cross(at.Heading, off);
        beyondM = 0.0;
        if (!Closed && ((s <= 0.0 && along < 0.0) || (s >= LengthM && along > 0.0))) beyondM = Math.Abs(along);
        else if (Math.Abs(along) > 1e-4) d = d < 0.0 ? -off.Len : off.Len;
        return true;
    }
}
