namespace KSACars;

/// <summary>
/// A car driven in another game, as the states it has sent: each put on this game's clock, and the
/// car drawn a little in the past, between the two states either side of then, so a datagram late
/// or lost is not a jump. Past the newest state it is carried on at its own velocity for a moment,
/// and then held.
///
/// <para>The sender's clock is put on this one by the least that has been seen between a state's
/// sending and its arrival over the last few seconds: the quickest a datagram has come is the one
/// least held up, and the window lets a clock that drifts be followed.</para>
/// </summary>
internal sealed class RemoteTrack
{
    /// <summary>How far in the past the car is drawn, s: three states at the thirty a second each game sends, so one lost is bridged.</summary>
    public const double DelaySeconds = 0.1;

    /// <summary>How long past its newest state a car is carried on before it is held where that leaves it, s.</summary>
    public const double CarryOnSeconds = 0.3;

    // How long a state's lag is remembered for the clocks' offset, s.
    private const double OffsetWindowSeconds = 5.0;

    private const int Kept = 32;

    private readonly List<(double At, NetWire.State State)> _states = [];
    private readonly Queue<(double Arrived, double Lag)> _lags = new();
    private uint _newest;
    private bool _any;

    /// <summary>When the newest state arrived, on this game's clock, s.</summary>
    public double HeardAt { get; private set; } = double.NegativeInfinity;

    public NetWire.State? Newest => _states.Count > 0 ? _states[^1].State : null;

    /// <summary>A state as it arrives. One older than the newest is late, and is put in its place; a repeat is dropped.</summary>
    public void Add(NetWire.State state, double arrived)
    {
        if (_any && state.Sequence == _newest) return;
        if (_any && unchecked((int)(state.Sequence - _newest)) < -1000) _states.Clear();

        _lags.Enqueue((arrived, arrived - state.Sent));
        while (_lags.Count > 1 && _lags.Peek().Arrived < arrived - OffsetWindowSeconds) _lags.Dequeue();
        double offset = _lags.Min(l => l.Lag);

        // Everything is put on the clock again, so a better offset moves the old states with the new.
        for (int i = 0; i < _states.Count; i++) _states[i] = (_states[i].State.Sent + offset, _states[i].State);
        int at = _states.Count;
        while (at > 0 && unchecked((int)(_states[at - 1].State.Sequence - state.Sequence)) > 0) at--;
        if (at > 0 && _states[at - 1].State.Sequence == state.Sequence) return;
        _states.Insert(at, (state.Sent + offset, state));
        if (_states.Count > Kept) _states.RemoveAt(0);
        if (!_any || unchecked((int)(state.Sequence - _newest)) > 0) _newest = state.Sequence;
        _any = true;
        HeardAt = arrived;
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
