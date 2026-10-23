namespace KSACars;

/// <summary>
/// A car driven in another game, as the states it has sent: each put on this game's clock, and the
/// car drawn a little in the past, between the two states either side of then, so a datagram late
/// or lost is not a jump. Past the newest state it is carried on at its own velocity for a moment,
/// and then held.
///
/// <para>The sender's clock is put on this one by the least that has been seen between a state's
/// sending and its arrival over the last few seconds, the quickest a datagram has come being the one
/// least held up, along a line: two worlds that run at different rates have a lag that grows or
/// shrinks with time, and the least of it over the window would be seconds stale.</para>
/// </summary>
internal sealed class RemoteTrack
{
    /// <summary>How far in the past the car is drawn, s: three states at the thirty a second each game sends, so one lost is bridged.</summary>
    public const double DelaySeconds = 0.1;

    /// <summary>How long past its newest state a car is carried on before it is held where that leaves it, s.</summary>
    public const double CarryOnSeconds = 0.3;

    // How long a state's lag is remembered for the clocks' offset, s.
    private const double OffsetWindowSeconds = 5.0;

    // The lag is fitted through the least of each of these spells of arrivals, s, once it has a few over this long.
    private const double BinSeconds = 0.5, FitOverSeconds = 1.5;
    private const int FitBins = 4;

    // The sender's world runs between half and twice as fast as this one's, or the fit is not believed.
    private const double LeastSlope = -0.5, MostSlope = 1.0;

    private const int Kept = 32;

    private readonly List<(double At, NetWire.State State)> _states = [];
    private readonly Queue<(double Arrived, double Sent, double Lag)> _lags = new();
    private (double Sent, double Lag, double Slope) _fit;
    private uint _newest;
    private bool _any;

    /// <summary>Forgets every state and what the clocks were measured to be apart.</summary>
    public void Clear()
    {
        _states.Clear();
        _lags.Clear();
        _fit = default;
        (_any, _newest, HeardAt) = (false, 0, double.NegativeInfinity);
    }

    /// <summary>When the newest state arrived, on this game's clock, s.</summary>
    public double HeardAt { get; private set; } = double.NegativeInfinity;

    public NetWire.State? Newest => _states.Count > 0 ? _states[^1].State : null;

    /// <summary>A state as it arrives. One older than the newest is late, and is put in its place; a repeat is dropped.</summary>
    public void Add(NetWire.State state, double arrived)
    {
        if (_any && state.Sequence == _newest) return;
        if (_any && unchecked((int)(state.Sequence - _newest)) < -1000) _states.Clear();

        _lags.Enqueue((arrived, state.Sent, arrived - state.Sent));
        while (_lags.Count > 1 && _lags.Peek().Arrived < arrived - OffsetWindowSeconds) _lags.Dequeue();
        _fit = Fit(state.Sent);

        // Everything is put on the clock again, so a better offset moves the old states with the new.
        for (int i = 0; i < _states.Count; i++) _states[i] = (Here(_states[i].State.Sent), _states[i].State);
        int at = _states.Count;
        while (at > 0 && unchecked((int)(_states[at - 1].State.Sequence - state.Sequence)) > 0) at--;
        if (at > 0 && _states[at - 1].State.Sequence == state.Sequence) return;
        _states.Insert(at, (Here(state.Sent), state));
        if (_states.Count > Kept) _states.RemoveAt(0);
        if (!_any || unchecked((int)(state.Sequence - _newest)) > 0) _newest = state.Sequence;
        _any = true;
        HeardAt = arrived;
    }

    // A time on the sender's clock on this one.
    private double Here(double sent) => sent + _fit.Lag + (_fit.Slope * (sent - _fit.Sent));

    // The lag as a line in the sender's time, under every spell's least: its slope fitted through them, and lowered to the lowest.
    private (double Sent, double Lag, double Slope) Fit(double newest)
    {
        (double Sent, double Lag)[] least = [.. _lags.GroupBy(l => Math.Floor(l.Arrived / BinSeconds)).Select(g => g.MinBy(l => l.Lag)).Select(l => (l.Sent - newest, l.Lag))];
        double slope = 0.0;
        if (least.Length >= FitBins && least.Max(l => l.Sent) - least.Min(l => l.Sent) >= FitOverSeconds)
        {
            double mx = least.Average(l => l.Sent), my = least.Average(l => l.Lag);
            double sxx = least.Sum(l => (l.Sent - mx) * (l.Sent - mx)), sxy = least.Sum(l => (l.Sent - mx) * (l.Lag - my));
            slope = Math.Clamp(sxy / sxx, LeastSlope, MostSlope);
        }
        return (newest, least.Min(l => l.Lag - (slope * l.Sent)), slope);
    }

    /// <summary>The car as it is to be drawn at a time on this game's clock, with the state its inputs are taken from; null before any has come.</summary>
    public (RacePose Pose, NetWire.State Inputs)? At(double now)
    {
        if (_states.Count == 0) return null;
        double then = now - DelaySeconds;
        if (then <= _states[0].At) return (_states[0].State.Pose, _states[0].State);
        for (int i = _states.Count - 1; i > 0; i--)
        {
            (double t1, NetWire.State b) = _states[i];
            (double t0, NetWire.State a) = _states[i - 1];
            if (then < t0) continue;
            if (then > t1) break;
            double share = t1 > t0 ? (then - t0) / (t1 - t0) : 1.0;
            return (RacePose.Between(a.Pose, b.Pose, share), share < 0.5 ? a : b);
        }
        (double last, NetWire.State newest) = _states[^1];
        return (newest.Pose.Ahead(Math.Min(then - last, CarryOnSeconds)), newest);
    }
}
