namespace KSACars;

/// <summary>
/// How fast a clock really goes against another, measured over the last few seconds: a world's universe
/// time against the wall, which KSA's own achieved fraction does not see when it is held back by drawing.
/// </summary>
internal sealed class PaceMeter(double windowSeconds = 4.0)
{
    // A reading this far behind the last is a clock set back, as by a world loaded, and the measuring starts again; less is one that came late, and is left out.
    private const double SetBackSeconds = 1.0;

    private readonly Queue<(double Wall, double Clock)> _samples = new();
    private (double Wall, double Clock) _last;

    public void Clear() => _samples.Clear();

    /// <summary>One reading of the clock at a time on the wall, s.</summary>
    public void Add(double wall, double clock)
    {
        if (_samples.Count > 0 && (clock < _last.Clock || wall < _last.Wall))
        {
            if (clock > _last.Clock - SetBackSeconds) return;
            _samples.Clear();
        }
        _last = (wall, clock);
        _samples.Enqueue(_last);
        while (_samples.Count > 2 && _samples.Peek().Wall < wall - windowSeconds) _samples.Dequeue();
    }

    /// <summary>
    /// Clock seconds a wall second over the window, or null before a second of it has been seen: the slope
    /// of a line through every reading, so readings taken when datagrams arrive, early or late, average out.
    /// </summary>
    public double? Rate
    {
        get
        {
            if (_samples.Count < 3 || _last.Wall - _samples.Peek().Wall < 1.0) return null;
            (double w0, double c0) = _samples.Peek();
            double mw = 0.0, mc = 0.0;
            foreach ((double w, double c) in _samples) (mw, mc) = (mw + (w - w0), mc + (c - c0));
            (mw, mc) = (mw / _samples.Count, mc / _samples.Count);
            double sww = 0.0, swc = 0.0;
            foreach ((double w, double c) in _samples)
            {
                double dw = w - w0 - mw;
                (sww, swc) = (sww + (dw * dw), swc + (dw * (c - c0 - mc)));
            }
            return sww > 0.0 ? swc / sww : null;
        }
    }
}

/// <summary>
/// The pace a host holds its race to, so a guest whose world cannot keep real time is not left behind:
/// dropped under the slowest guest's measured pace once that guest has been short of the host's for a
/// moment and is behind, and raised again slowly while every guest keeps up. Every guest follows the
/// host's measured pace, so the host's world running slower is all it takes.
/// </summary>
internal sealed class SharedPace
{
    /// <summary>The slowest the race is ever held to; a world slower than this falls behind.</summary>
    public const double Floor = 0.5;

    // A guest this far under the host's pace, for this long, is short; the race is then held a little under it. One that says it is
    // behind is short by any measurable amount, since it is falling further behind, which a game that does not say cannot be told by.
    private const double ShortBy = 0.03, BehindShortBy = 0.005, ShortForSeconds = 2.0, Under = 0.97;

    // Behind by more than this a short guest is falling behind; under the second, it has caught up, s.
    private const double BehindSeconds = 0.5, CaughtUpSeconds = 0.3;

    /// <summary>How fast the pace is raised again while everyone keeps up, a second.</summary>
    public const double RisePerSecond = 0.01;

    private double _short;

    /// <summary>The pace the host's world is to run at, 1 being real time.</summary>
    public double Pace { get; private set; } = 1.0;

    /// <param name="hostRate">The host's own world, measured, universe seconds a wall second; null before it is known.</param>
    /// <param name="guests">Each guest's measured pace and how far behind the host's time it says it is, NaN for a game that does not say.</param>
    public double Update(double dt, double? hostRate, IEnumerable<(double? Rate, double Behind)> guests)
    {
        (double Rate, double Behind)[] known = [.. guests.Where(g => g.Rate is { } r && double.IsFinite(r)).Select(g => (g.Rate!.Value, g.Behind))];
        if (known.Length == 0 || hostRate is not { } host)
        {
            (Pace, _short) = (1.0, 0.0);
            return Pace;
        }
        (double slowest, double behind) = known.MinBy(g => g.Rate);
        bool falling = slowest < host - (double.IsFinite(behind) ? BehindShortBy : ShortBy) && !(behind < BehindSeconds);
        if (falling)
        {
            _short += dt;
            if (_short >= ShortForSeconds)
            {
                Pace = Math.Clamp(Math.Min(Pace, slowest * Under), Floor, 1.0);
                _short = 0.0;
            }
            return Pace;
        }
        _short = 0.0;
        // Against the pace set as well as the host's measured, which trails it by the meter's window and would let the pace rise past the guest.
        double over = Math.Max(host, Pace) - (ShortBy / 3.0);
        bool keepingUp = known.All(g => !(g.Rate < over) && !(g.Behind >= CaughtUpSeconds));
        if (keepingUp) Pace = Math.Min(1.0, Pace + (RisePerSecond * dt));
        return Pace;
    }
}
