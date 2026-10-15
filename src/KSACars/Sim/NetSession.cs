using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace KSACars;

/// <summary>
/// A race between games over one UDP socket each: a host that the others join, which hands each a
/// number and a box of the grid, says who is in the race, passes every car's state on to everyone
/// else, and starts the race. A client speaks to the host alone, so only the host has to be
/// reachable, and a client behind a router is reached on the way its own datagrams opened.
///
/// <para>Datagrams are read on a thread of the socket's own and handled in <see cref="Update"/>, from
/// whoever runs the race, so nothing here is touched from two threads but the queue between them.
/// The clock is the caller's, in seconds, so a test can run two sessions on one machine.</para>
/// </summary>
internal sealed class NetSession : IDisposable
{
    public enum Role { Host, Client }

    /// <summary>Someone in the race, this game's own player among them.</summary>
    public sealed class Racer(byte id, string name, string car, byte box)
    {
        public byte Id { get; } = id;
        public string Name { get; set; } = name;
        public string Car { get; set; } = car;
        public byte Box { get; set; } = box;

        /// <summary>Where the host hears this racer from; nothing for the host itself, or seen from a client.</summary>
        public IPEndPoint? At { get; set; }

        public double HeardAt { get; set; }
        public RemoteTrack Track { get; } = new();
    }

    public const byte HostId = 1;

    /// <summary>The most in a race: the grid has ten boxes.</summary>
    public const int MostRacers = 10;

    // A hello is sent this often until it is answered, and given up after so long.
    private const double HelloEverySeconds = 0.5, JoinWithinSeconds = 15.0;

    private const double PingEverySeconds = 1.0, RosterEverySeconds = 2.0;

    // A racer not heard from for this long has gone.
    private const double SilentSeconds = 10.0;

    // A punch is sent this often, for so long, to open the way through the routers on both sides.
    private const double PunchEverySeconds = 0.25, PunchForSeconds = 30.0;

    // A start is said again this often until the lights go out, since a datagram can be lost.
    private const double StartEverySeconds = 0.25;

    private const int MostQueued = 2048;

    private readonly Socket _socket;
    private readonly Thread _reader;
    private readonly ConcurrentQueue<(byte[] Data, IPEndPoint From, double Arrived)> _in = new();
    private readonly Func<double> _clock;
    private readonly byte[] _out = new byte[NetWire.BufferBytes];
    private readonly Dictionary<byte, Racer> _racers = [];
    private readonly List<(IPEndPoint To, double Until, double Next)> _punches = [];
    private readonly byte[] _stunTransaction = new byte[12];
    private readonly List<string> _said = [];

    private IPEndPoint? _host;
    private double _helloNext, _joinUntil, _pingNext, _rosterNext, _startNext;
    private double _bestRtt = double.PositiveInfinity;
    private uint _sequence, _races;
    private volatile bool _closed;

    public Role Is { get; }
    public string Name { get; }
    public string Car { get; private set; }

    /// <summary>The course the host is racing on and its hash; on the host what it has laid, on a client what the roster says.</summary>
    public string Course { get; private set; } = "";
    public string CourseHash { get; private set; } = "";

    /// <summary>The number this game's own car is known by, zero until a client is welcomed.</summary>
    public byte Me { get; private set; }

    /// <summary>Whether a client is in the race, or a host is listening.</summary>
    public bool Joined => Me != 0;

    /// <summary>Why a client is out of the race, once it is: refused, never answered, or the host gone.</summary>
    public string Out { get; private set; } = "";

    /// <summary>The port the socket is on.</summary>
    public int Port { get; }

    /// <summary>Where this game is seen from outside its routers, once a STUN server has said.</summary>
    public IPEndPoint? Public { get; private set; }

    /// <summary>The host's clock less this game's, s: zero on the host.</summary>
    public double HostOffset { get; private set; }

    /// <summary>Whether <see cref="HostOffset"/> has been measured: always on the host, and on a client once a ping has been answered.</summary>
    public bool ClockKnown => Is == Role.Host || double.IsFinite(_bestRtt);

    /// <summary>The last start the host has said, and when the lights go out on this game's clock.</summary>
    public (uint Race, double At, byte Laps)? Start { get; private set; }

    public IReadOnlyCollection<Racer> Racers => _racers.Values;

    public NetSession(Role role, string name, string car, int port, Func<double> clock)
    {
        (Is, Name, Car, _clock) = (role, Clip(name), car, clock);
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        // Windows reports an ICMP refusal of an earlier send as a failure of the next receive.
        if (OperatingSystem.IsWindows()) _socket.IOControl(unchecked((int)0x9800000C), [0, 0, 0, 0], null);
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));
        Port = ((IPEndPoint)_socket.LocalEndPoint!).Port;
        Random.Shared.NextBytes(_stunTransaction);

        if (role == Role.Host)
        {
            Me = HostId;
            _racers[HostId] = new Racer(HostId, Name, car, 0) { HeardAt = clock() };
        }
        _reader = new Thread(Read) { IsBackground = true, Name = "KSACars race socket", Priority = ThreadPriority.AboveNormal };
        _reader.Start();
    }

    /// <summary>What has happened since it was last asked, in words for the panel and the log.</summary>
    public string[] Said()
    {
        string[] said = [.. _said];
        _said.Clear();
        return said;
    }

    /// <summary>The course a host races on, and the car this game's player drives; told to everyone when either changes.</summary>
    public void Set(string course, string courseHash, string car)
    {
        if (Is == Role.Host && (course != Course || courseHash != CourseHash))
        {
            (Course, CourseHash) = (course, courseHash);
            _rosterNext = 0.0;
        }
        if (car != Car)
        {
            Car = car;
            if (_racers.TryGetValue(Me, out Racer? me)) me.Car = car;
            _rosterNext = 0.0;
            _helloNext = 0.0;
        }
    }

    /// <summary>Has a client ask to join a host, at an address that has been looked up.</summary>
    public void Join(IPEndPoint host)
    {
        if (Is != Role.Client) return;
        (_host, Me, Out, _helloNext, _joinUntil) = (host, 0, "", 0.0, _clock() + JoinWithinSeconds);
        _racers.Clear();
    }

    /// <summary>Sends punches to an address for a while, so its router lets that game's datagrams in: for a host that cannot open its own port.</summary>
    public void PunchTo(IPEndPoint to)
    {
        double now = _clock();
        _punches.RemoveAll(p => p.To.Equals(to));
        _punches.Add((to, now + PunchForSeconds, now));
        Say($"opening the way to {to} for {PunchForSeconds:F0} s");
    }

    /// <summary>Asks the public STUN servers where this game is seen from outside: the answer is <see cref="Public"/>.</summary>
    public async Task AskWhereAsync(CancellationToken token)
    {
        byte[] ask = Stun.Request(_stunTransaction);
        foreach ((string host, int port) in Stun.Servers)
        {
            if (Public is not null || _closed) return;
            try
            {
                IPAddress[] found = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, token);
                if (found.Length == 0) continue;
                for (int k = 0; k < 3 && Public is null && !_closed; k++)
                {
                    _socket.SendTo(ask, new IPEndPoint(found[0], port));
                    await Task.Delay(400, token);
                }
            }
            catch (Exception e) when (e is SocketException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }
    }

    /// <summary>A host starts a race: the lights go out so many seconds from now, on every game's clock.</summary>
    public void StartRace(double inSeconds, byte laps)
    {
        if (Is != Role.Host) return;
        Start = (++_races, _clock() + inSeconds, laps);
        _startNext = 0.0;
        Say($"race {_races} starts in {inSeconds:F0} s");
    }

    /// <summary>This game's own car where it is, to the host or, from the host, to everyone.</summary>
    public void Send(RacePose pose, float throttle, float steer, float brake, byte gear, bool lights)
    {
        if (!Joined) return;
        NetWire.State state = new(Me, ++_sequence, _clock(), pose, throttle, steer, brake, gear, lights);
        if (Is == Role.Client) SendTo(state, _host);
        else foreach (Racer r in _racers.Values) SendTo(state, r.At);
    }

    /// <summary>Handles what has arrived and sends what is due. From whoever runs the race, once a frame.</summary>
    public void Update()
    {
        double now = _clock();
        int handled = 0;
        while (handled++ < MostQueued && _in.TryDequeue(out (byte[] Data, IPEndPoint From, double Arrived) d)) Handle(d.Data, d.From, d.Arrived);

        for (int i = _punches.Count - 1; i >= 0; i--)
        {
            (IPEndPoint to, double until, double next) = _punches[i];
            if (now > until || _racers.Values.Any(r => to.Equals(r.At)))
            {
                _punches.RemoveAt(i);
                continue;
            }
            if (now < next) continue;
            SendTo(new NetWire.Punch(), to);
            _punches[i] = (to, until, now + PunchEverySeconds);
        }

        if (Is == Role.Client) UpdateClient(now);
        else UpdateHost(now);
    }

    private void UpdateClient(double now)
    {
        if (_host is null || Out.Length > 0) return;
        if (!Joined)
        {
            if (now > _joinUntil)
            {
                Out = $"{_host} never answered: is the code right, and has the host pressed Host?";
                Say(Out);
                return;
            }
            if (now >= _helloNext)
            {
                SendTo(new NetWire.Hello(Name, Car, Course, CourseHash), _host);
                _helloNext = now + HelloEverySeconds;
            }
            return;
        }
        if (now >= _pingNext)
        {
            SendTo(new NetWire.Ping(now), _host);
            _pingNext = now + PingEverySeconds;
        }
        // A car changed after joining is said in a hello again, which the host takes as the same racer.
        if (_helloNext < double.PositiveInfinity && now >= _helloNext)
        {
            SendTo(new NetWire.Hello(Name, Car, Course, CourseHash), _host);
            _helloNext = double.PositiveInfinity;
        }
        if (_racers.TryGetValue(HostId, out Racer? host) && now - host.HeardAt > SilentSeconds)
        {
            Out = "the host has not been heard from for ten seconds";
            Say(Out);
            Me = 0;
        }
    }

    private void UpdateHost(double now)
    {
        foreach (Racer gone in _racers.Values.Where(r => r.Id != HostId && now - r.HeardAt > SilentSeconds).ToArray())
        {
            _racers.Remove(gone.Id);
            Say($"{gone.Name} has gone quiet and is out of the race");
            _rosterNext = 0.0;
        }
        if (now >= _rosterNext)
        {
            NetWire.Roster roster = Roster();
            foreach (Racer r in _racers.Values) SendTo(roster, r.At);
            _rosterNext = now + RosterEverySeconds;
        }
        if (Start is { } start && now < start.At && now >= _startNext)
        {
            NetWire.Start said = new(start.Race, start.At, start.Laps);
            foreach (Racer r in _racers.Values) SendTo(said, r.At);
            _startNext = now + StartEverySeconds;
        }
    }

    private NetWire.Roster Roster() =>
        new(Course, CourseHash, [.. _racers.Values.OrderBy(r => r.Box).Select(r => new NetWire.Racer(r.Id, r.Name, r.Car, r.Box))]);

    private void Handle(byte[] data, IPEndPoint from, double arrived)
    {
        if (Stun.IsStun(data))
        {
            if (Stun.Answer(data, _stunTransaction) is { } seen && Public is null)
            {
                Public = seen;
                Say($"seen from outside at {seen}");
            }
            return;
        }
        if (NetWire.Read(data, out ushort version) is not { } message)
        {
            if (version != 0 && version != NetWire.Version && Is == Role.Host && _racers.Values.All(r => !from.Equals(r.At)))
            {
                SendTo(new NetWire.Welcome(0, $"the host has version {NetWire.Version} of the race and you have {version}: you each need the same Fast & Purrious"), from);
            }
            return;
        }
        if (Is == Role.Host) HandleAsHost(message, from, arrived);
        else if (from.Equals(_host)) HandleAsClient(message, arrived);
    }

    private void HandleAsHost(NetWire.Message message, IPEndPoint from, double arrived)
    {
        Racer? racer = _racers.Values.FirstOrDefault(r => from.Equals(r.At));
        if (racer is not null) racer.HeardAt = arrived;
        switch (message)
        {
            case NetWire.Hello hello:
                if (racer is not null)
                {
                    (racer.Name, racer.Car) = (Clip(hello.Name), hello.Car);
                    SendTo(new NetWire.Welcome(racer.Id, ""), from);
                    _rosterNext = 0.0;
                    return;
                }
                if (_racers.Count >= MostRacers)
                {
                    SendTo(new NetWire.Welcome(0, $"the race is full: {MostRacers} is the most the grid holds"), from);
                    return;
                }
                byte id = HostId;
                while (_racers.ContainsKey(id)) id++;
                byte box = 0;
                while (_racers.Values.Any(r => r.Box == box)) box++;
                Racer joined = new(id, UniqueName(Clip(hello.Name)), hello.Car, box) { At = from, HeardAt = arrived };
                _racers[id] = joined;
                SendTo(new NetWire.Welcome(id, ""), from);
                _rosterNext = 0.0;
                Say($"{joined.Name} has joined, in box {box + 1}, from {from}");
                return;
            case NetWire.State state when racer is not null && state.Id == racer.Id:
                racer.Track.Add(state, arrived);
                foreach (Racer other in _racers.Values) if (other != racer) SendTo(state, other.At);
                return;
            case NetWire.Ping ping when racer is not null:
                SendTo(new NetWire.Pong(ping.Sent, _clock()), from);
                return;
            case NetWire.Bye when racer is not null:
                _racers.Remove(racer.Id);
                _rosterNext = 0.0;
                Say($"{racer.Name} has left");
                return;
        }
    }

    private void HandleAsClient(NetWire.Message message, double arrived)
    {
        if (_racers.TryGetValue(HostId, out Racer? host)) host.HeardAt = arrived;
        switch (message)
        {
            case NetWire.Welcome w when !Joined:
                if (w.Id == 0)
                {
                    Out = $"refused: {w.Refused}";
                    Say(Out);
                    _host = null;
                    return;
                }
                Me = w.Id;
                _pingNext = 0.0;
                _helloNext = double.PositiveInfinity;
                Say($"in the race as racer {w.Id}");
                return;
            case NetWire.Roster roster when Joined:
                if (roster.Course != Course || roster.CourseHash != CourseHash)
                {
                    (Course, CourseHash) = (roster.Course, roster.CourseHash);
                    Say(roster.Course.Length > 0 ? $"the host is racing on {roster.Course}" : "the host has no course laid");
                }
                HashSet<byte> listed = [];
                foreach (NetWire.Racer r in roster.Racers)
                {
                    listed.Add(r.Id);
                    if (!_racers.TryGetValue(r.Id, out Racer? known))
                    {
                        _racers[r.Id] = known = new Racer(r.Id, r.Name, r.Car, r.Box) { HeardAt = arrived };
                        if (r.Id != Me) Say($"{r.Name} is in the race");
                    }
                    (known.Name, known.Car, known.Box) = (r.Name, r.Car, r.Box);
                    if (r.Id == HostId) known.HeardAt = arrived;
                }
                foreach (byte gone in _racers.Keys.Where(k => !listed.Contains(k)).ToArray())
                {
                    Say($"{_racers[gone].Name} has left");
                    _racers.Remove(gone);
                }
                return;
            case NetWire.State state when Joined && state.Id != Me && _racers.TryGetValue(state.Id, out Racer? whose):
                whose.Track.Add(state, arrived);
                whose.HeardAt = arrived;
                return;
            case NetWire.Pong pong:
                double rtt = arrived - pong.Echo;
                // The quickest answer is the one least held up on the way, so it says the clocks' difference best.
                if (rtt >= 0.0 && rtt <= _bestRtt * 1.5)
                {
                    if (rtt < _bestRtt) _bestRtt = rtt;
                    HostOffset = pong.Answered + (0.5 * rtt) - arrived;
                }
                return;
            case NetWire.Start s when Joined && (Start is not { } had || had.Race != s.Race || Math.Abs(had.At - (s.At - HostOffset)) > 0.02):
                bool fresh = Start is not { } was || was.Race != s.Race;
                Start = (s.Race, s.At - HostOffset, s.Laps);
                if (fresh) Say($"the host has started race {s.Race}");
                return;
            case NetWire.Bye:
                Out = "the host has stopped the race";
                Say(Out);
                Me = 0;
                return;
        }
    }

    private string UniqueName(string name)
    {
        string unique = name.Length > 0 ? name : "Kitten";
        for (int k = 2; _racers.Values.Any(r => r.Name == unique); k++) unique = $"{name} {k}";
        return unique;
    }

    private static string Clip(string? name)
    {
        string n = (name ?? "").Trim();
        return n.Length > NetWire.NameChars ? n[..NetWire.NameChars] : n;
    }

    private void SendTo(NetWire.Message message, IPEndPoint? to)
    {
        if (to is null || _closed) return;
        try
        {
            int n = NetWire.Write(message, _out);
            _socket.SendTo(_out.AsSpan(0, n), SocketFlags.None, to);
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException or ArgumentException)
        {
        }
    }

    private void Say(string what) => _said.Add(what);

    private void Read()
    {
        byte[] buffer = new byte[NetWire.BufferBytes];
        EndPoint any = new IPEndPoint(IPAddress.Any, 0);
        while (!_closed)
        {
            try
            {
                int n = _socket.ReceiveFrom(buffer, ref any);
                if (_in.Count < MostQueued) _in.Enqueue((buffer[..n], (IPEndPoint)any, _clock()));
            }
            // Anything thrown here would end the game: an unhandled exception on any thread does.
            catch (Exception)
            {
                if (_closed) return;
                Thread.Sleep(10);
            }
        }
    }

    /// <summary>Says goodbye and closes the socket.</summary>
    public void Dispose()
    {
        if (_closed) return;
        if (Is == Role.Client) SendTo(new NetWire.Bye(), _host);
        else foreach (Racer r in _racers.Values) SendTo(new NetWire.Bye(), r.At);
        _closed = true;
        _socket.Dispose();
        _reader.Join(500);
    }
}
