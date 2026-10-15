using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace KSACars;

/// <summary>
/// A race between games over one UDP socket each: a host that the others join, which hands each a
/// number, a box of the grid and a copy of its world, says who is in the race and what its universe
/// time is, passes every car's state on to everyone else, and starts the race. A client speaks to the host alone, so only the host has to be
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

        /// <summary>The Id of this racer's car in every copy of the world, once it has one.</summary>
        public string Craft { get; set; } = "";

        /// <summary>On the host, whether this racer has loaded the world it was sent and is racing in it.</summary>
        public bool HasWorld { get; set; }

        /// <summary>
        /// On the host, whether this racer has said anything but hello: a guest looking on its own network
        /// is heard at two addresses and settles on the first to answer, and the other never speaks again.
        /// </summary>
        public bool Confirmed { get; set; }
    }

    public const byte HostId = 1;

    /// <summary>The most in a race: the grid has ten boxes.</summary>
    public const int MostRacers = 10;

    // A hello is sent this often until it is answered, and given up after so long.
    private const double HelloEverySeconds = 0.5, JoinWithinSeconds = 15.0;

    private const double PingEverySeconds = 1.0, RosterEverySeconds = 2.0;

    // A ping slower than this to come back says nothing of the clocks: a game loading a world stands still for seconds with one on its way.
    private const double ClockRttSeconds = 0.5;

    // A racer not heard from for this long has gone; one loading the world it was sent is given longer, since a load can hold its game still.
    private const double SilentSeconds = 10.0, LoadingSilentSeconds = 90.0;

    // A punch is sent this often, for so long, to open the way through the routers on both sides.
    private const double PunchEverySeconds = 0.25, PunchForSeconds = 30.0;

    // A start is said again this often until the lights go out, since a datagram can be lost.
    private const double StartEverySeconds = 0.25;

    // The host's universe time is said this often.
    private const double ClockEverySeconds = 0.2;

    // A guest asks again for what is missing of a world this often, and for all of it after so long with no piece.
    private const double NeedEverySeconds = 0.25, QuietSeconds = 1.0;

    // A guest says it has loaded the world this often until the host's world runs again.
    private const double LoadedEverySeconds = 0.5;

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

    private readonly Dictionary<byte, WorldTransfer.Outgoing> _going = [];
    private readonly List<Racer> _arriving = [];
    private WorldTransfer.Incoming? _coming;
    private uint _transfers, _loadedTransfer;
    private double _clockNext, _needNext, _lastPieceAt, _loadedNext, _sentAt;
    private (double Universe, double Sent, double Speed)? _hostClock;
    private double _hostRate;

    // How much of each new measure of the host's pace is taken, so one slow frame there does not swing a guest's world.
    private const double RateSmoothing = 0.3;

    private IPEndPoint? _host;
    private IPEndPoint[] _nearby = [];
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

    /// <summary>The KSA build this game runs, which a guest's has to match the host's.</summary>
    public string KsaBuild { get; init; } = "";

    /// <summary>Every mod this game has enabled: a guest has to have every one the host has, or the host's world will not load.</summary>
    public IReadOnlyCollection<string> Mods { get; init; } = [];

    /// <summary>On the host, its universe time and how fast its world runs, asked whenever they are said.</summary>
    public Func<(double Universe, double Speed)>? UniverseClock { get; set; }

    /// <summary>On a guest, the host's world once every piece of it has come, and which sending it was.</summary>
    public (uint Transfer, byte[] Data)? World { get; private set; }

    /// <summary>On a guest, how much of the host's world has come, 0 to 1, while it is coming.</summary>
    public double? WorldShare => _coming is { Complete: false } c ? (double)c.Have / c.Count : null;

    /// <summary>On the host, whether any racer is being sent a world or has not yet loaded it, which its world is held for.</summary>
    public bool Handing => _arriving.Any(r => r.Confirmed) || _racers.Values.Any(r => r.Id != HostId && r.Confirmed && !r.HasWorld);

    /// <summary>On the host, the next racer that has been let in and needs a world: dequeued as it is taken.</summary>
    public Racer? NextArrival()
    {
        _arriving.RemoveAll(r => !_racers.ContainsKey(r.Id));
        if (_arriving.FirstOrDefault(r => r.Confirmed) is not { } next) return null;
        _arriving.Remove(next);
        return next;
    }

    /// <summary>On the host, sends a racer the world it is to race in.</summary>
    public void SendWorld(Racer racer, byte[] world)
    {
        _going[racer.Id] = new WorldTransfer.Outgoing(++_transfers, world);
        _sentAt = _clock();
        Say($"sending {racer.Name} the world, {world.Length / 1024} KB");
    }

    /// <summary>On the host, the Id of a racer's car in the world, told to everyone.</summary>
    public void SetCraft(byte racer, string craft)
    {
        if (!_racers.TryGetValue(racer, out Racer? r) || r.Craft == craft) return;
        r.Craft = craft;
        _rosterNext = 0.0;
    }

    /// <summary>On a guest, says the world it was sent is loaded and its course laid, until the host's world runs again.</summary>
    public void SayLoaded()
    {
        if (World is { } w) (_loadedTransfer, _loadedNext) = (w.Transfer, 0.0);
    }

    /// <summary>
    /// On a guest, the host's universe time now and how fast it is really going, once the host has said
    /// it and the clocks are measured: what the host says carried on at the pace its last two words
    /// show, which is under its speed while it draws too slowly to keep up.
    /// </summary>
    public (double Universe, double Speed)? HostUniverse =>
        _hostClock is { } c && ClockKnown ? (ClockLock.HostNow(c.Universe, c.Sent, _hostRate, _clock() + HostOffset), _hostRate) : null;

    /// <summary>
    /// The clock every car's states are timed and drawn on, read when one is sent and when one is
    /// handled: the universe time, which every world in the race shares, so a car is drawn as its
    /// world's physics moves and not as the wall clock does. This game's own clock if none is set.
    /// </summary>
    public Func<double>? TrackClock { get; set; }

    /// <summary>Forgets every car's states: for a guest whose world, and with it its universe time, has just been replaced.</summary>
    public void ForgetTracks()
    {
        foreach (Racer r in _racers.Values) r.Track.Clear();
    }

    public NetSession(Role role, string name, string car, int port, Func<double> clock)
    {
        (Is, Name, Car, _clock) = (role, Clip(name), car, clock);
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        // Windows reports an ICMP refusal of an earlier send as a failure of the next receive.
        if (OperatingSystem.IsWindows()) _socket.IOControl(unchecked((int)0x9800000C), [0, 0, 0, 0], null);
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));
        _socket.EnableBroadcast = true;
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

    /// <summary>
    /// Has a client ask to join a host, at an address that has been looked up. With
    /// <paramref name="nearby"/> it asks this machine and the local network too, for a host behind the
    /// same router, which most routers will not reach by its own public address; the first host to
    /// answer on the same port is the one joined.
    /// </summary>
    public void Join(IPEndPoint host, bool nearby = false)
    {
        if (Is != Role.Client) return;
        _nearby = nearby
            ? [new IPEndPoint(IPAddress.Loopback, host.Port), new IPEndPoint(IPAddress.Broadcast, host.Port)]
            : [];
        (_host, Me, Out, _helloNext, _joinUntil) = (host, 0, "", 0.0, _clock() + JoinWithinSeconds);
        _racers.Clear();
        // A host started afresh numbers its worlds from one again, and its clock is another.
        (World, _coming, _hostClock, _hostRate, _loadedTransfer, _bestRtt, HostOffset, Start) = (null, null, null, 0.0, 0, double.PositiveInfinity, 0.0, null);
        Joins++;
    }

    /// <summary>On a guest, how many times it has asked to join: a new one is a new race, with a new world to take on.</summary>
    public int Joins { get; private set; }

    /// <summary>On the host, puts a racer out of the race and tells it why.</summary>
    public void Drop(Racer racer, string why)
    {
        if (racer.Id == HostId || !_racers.Remove(racer.Id)) return;
        _going.Remove(racer.Id);
        SendTo(new NetWire.Welcome(0, why), racer.At);
        SendTo(new NetWire.Bye(), racer.At);
        _rosterNext = 0.0;
        Say($"{racer.Name} is out of the race: {why}");
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

    /// <summary>
    /// This game's own car where it is, to the host or, from the host, to everyone; <paramref name="at"/>
    /// is the time it is there on <see cref="TrackClock"/>, this game's clock if none is set.
    /// </summary>
    public void Send(RacePose pose, float throttle, float steer, float brake, byte gear, bool lights, double? at = null)
    {
        if (!Joined) return;
        NetWire.State state = new(Me, ++_sequence, at ?? _clock(), pose, throttle, steer, brake, gear, lights);
        if (Is == Role.Client) SendTo(state, _host);
        else foreach (Racer r in _racers.Values) SendTo(state, r.At);
    }

    /// <summary>Handles what has arrived and sends what is due. From whoever runs the race, once a frame.</summary>
    public void Update()
    {
        double now = _clock();
        for (int i = 0; i < _late.Count; i++)
        {
            if (_late[i].Due > now) continue;
            Put(_late[i].Data, _late[i].To);
            _late.RemoveAt(i--);
        }
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
                NetWire.Hello hello = new(Name, Car, KsaBuild, string.Join(',', Mods));
                SendTo(hello, _host);
                foreach (IPEndPoint near in _nearby) SendTo(hello, near);
                _helloNext = now + HelloEverySeconds;
            }
            return;
        }
        if (now >= _pingNext)
        {
            SendTo(new NetWire.Ping(now), _host);
            _pingNext = now + PingEverySeconds;
        }
        if (_coming is { Complete: false } coming && now >= _needNext)
        {
            ushort[] missing = coming.Missing(all: now - _lastPieceAt > QuietSeconds);
            if (missing.Length > 0) SendTo(new NetWire.Need(coming.Id, missing), _host);
            _needNext = now + NeedEverySeconds;
        }
        if (_loadedTransfer != 0 && now >= _loadedNext)
        {
            SendTo(new NetWire.Loaded(_loadedTransfer), _host);
            _loadedNext = now + LoadedEverySeconds;
        }
        // A car changed after joining is said in a hello again, which the host takes as the same racer.
        if (_helloNext < double.PositiveInfinity && now >= _helloNext)
        {
            SendTo(new NetWire.Hello(Name, Car, KsaBuild, string.Join(',', Mods)), _host);
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
        foreach (Racer gone in _racers.Values.Where(r => r.Id != HostId && now - r.HeardAt > (r.HasWorld || !r.Confirmed ? SilentSeconds : LoadingSilentSeconds)).ToArray())
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
        if (UniverseClock is { } universe && now >= _clockNext && _racers.Count > 1)
        {
            (double u, double speed) = universe();
            NetWire.Clock clock = new(u, now, (float)speed);
            foreach (Racer r in _racers.Values) SendTo(clock, r.At);
            _clockNext = now + ClockEverySeconds;
        }
        double since = now - _sentAt;
        _sentAt = now;
        foreach ((byte id, WorldTransfer.Outgoing going) in _going.ToArray())
        {
            if (!_racers.TryGetValue(id, out Racer? to))
            {
                _going.Remove(id);
                continue;
            }
            foreach (NetWire.Chunk chunk in going.Next(since)) SendTo(chunk, to.At);
        }
        if (Start is { } start && now < start.At && now >= _startNext)
        {
            NetWire.Start said = new(start.Race, start.At, start.Laps);
            foreach (Racer r in _racers.Values) SendTo(said, r.At);
            _startNext = now + StartEverySeconds;
        }
    }

    private NetWire.Roster Roster() =>
        new(Course, CourseHash, [.. _racers.Values.OrderBy(r => r.Box).Select(r => new NetWire.Racer(r.Id, r.Name, r.Car, r.Box, r.Craft))]);

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
        else if (!Joined && _nearby.Length > 0 && _host is { } wanted && from.Port == wanted.Port && message is NetWire.Welcome
                 && (IPAddress.IsLoopback(from.Address) || JoinCode.IsPrivate(from.Address)))
        {
            // A host behind the same router, answering from this machine or the local network: it is the one joined.
            Say($"found the host on this network, at {from}");
            (_host, _nearby) = (from, []);
            HandleAsClient(message, arrived);
        }
    }

    private void HandleAsHost(NetWire.Message message, IPEndPoint from, double arrived)
    {
        Racer? racer = _racers.Values.FirstOrDefault(r => from.Equals(r.At));
        if (racer is not null)
        {
            racer.HeardAt = arrived;
            if (message is not NetWire.Hello) racer.Confirmed = true;
        }
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
                if (Refusal(hello) is { Length: > 0 } refused)
                {
                    SendTo(new NetWire.Welcome(0, refused), from);
                    return;
                }
                byte id = HostId;
                while (_racers.ContainsKey(id)) id++;
                byte box = 0;
                while (_racers.Values.Any(r => r.Box == box)) box++;
                Racer joined = new(id, UniqueName(Clip(hello.Name)), hello.Car, box) { At = from, HeardAt = arrived };
                _racers[id] = joined;
                _arriving.Add(joined);
                SendTo(new NetWire.Welcome(id, ""), from);
                _rosterNext = 0.0;
                Say($"{joined.Name} has joined, in box {box + 1}, from {from}");
                return;
            case NetWire.State state when racer is not null && state.Id == racer.Id:
                racer.Track.Add(state, TrackClock?.Invoke() ?? arrived);
                foreach (Racer other in _racers.Values) if (other != racer) SendTo(state, other.At);
                return;
            case NetWire.Ping ping when racer is not null:
                SendTo(new NetWire.Pong(ping.Sent, _clock()), from);
                return;
            case NetWire.Need need when racer is not null && _going.TryGetValue(racer.Id, out WorldTransfer.Outgoing? going) && going.Id == need.Transfer:
                going.Need(need.Missing);
                return;
            case NetWire.Got got when racer is not null && _going.TryGetValue(racer.Id, out WorldTransfer.Outgoing? sent) && sent.Id == got.Transfer:
                _going.Remove(racer.Id);
                Say($"{racer.Name} has the world");
                return;
            case NetWire.Loaded when racer is not null && !racer.HasWorld:
                racer.HasWorld = true;
                _going.Remove(racer.Id);
                _rosterNext = 0.0;
                Say($"{racer.Name} has loaded the world and is in the race");
                return;
            case NetWire.Bye when racer is not null:
                _going.Remove(racer.Id);
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
                    (known.Name, known.Car, known.Box, known.Craft) = (r.Name, r.Car, r.Box, r.Craft);
                    if (r.Id == HostId) known.HeardAt = arrived;
                }
                foreach (byte gone in _racers.Keys.Where(k => !listed.Contains(k)).ToArray())
                {
                    Say($"{_racers[gone].Name} has left");
                    _racers.Remove(gone);
                }
                return;
            case NetWire.State state when Joined && state.Id != Me && _racers.TryGetValue(state.Id, out Racer? whose):
                whose.Track.Add(state, TrackClock?.Invoke() ?? arrived);
                whose.HeardAt = arrived;
                return;
            case NetWire.Pong pong:
                double rtt = arrived - pong.Echo;
                // The quickest answer is the one least held up on the way, so it says the clocks' difference best.
                if (rtt >= 0.0 && rtt <= ClockRttSeconds && rtt <= _bestRtt * 1.5)
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
            case NetWire.Chunk chunk when Joined:
                if (World is { } whole && whole.Transfer == chunk.Transfer)
                {
                    // Every piece has come and the host is still sending: it has not heard so.
                    SendTo(new NetWire.Got(chunk.Transfer), _host);
                    return;
                }
                if (_coming is null || _coming.Id != chunk.Transfer)
                {
                    _coming = new WorldTransfer.Incoming(chunk.Transfer, chunk.Count);
                    Say($"receiving the host's world, {chunk.Count} pieces");
                }
                _coming.Add(chunk);
                _lastPieceAt = arrived;
                if (_coming.Complete)
                {
                    World = (_coming.Id, _coming.Assemble());
                    SendTo(new NetWire.Got(_coming.Id), _host);
                    Say($"the host's world has come, {World.Value.Data.Length / 1024} KB");
                }
                return;
            case NetWire.Clock clock when Joined:
                if (clock.Speed <= 0.0) _hostRate = 0.0;
                else if (_hostClock is { } last && clock.Sent - last.Sent > 0.05)
                {
                    double rate = Math.Clamp((clock.Universe - last.Universe) / (clock.Sent - last.Sent), 0.0, 2.0 * clock.Speed);
                    _hostRate = _hostRate > 0.0 ? _hostRate + (RateSmoothing * (rate - _hostRate)) : rate;
                }
                else if (_hostRate <= 0.0) _hostRate = clock.Speed;
                _hostClock = (clock.Universe, clock.Sent, clock.Speed);
                if (clock.Speed > 0.0 && _loadedTransfer != 0 && World is { } loaded && loaded.Transfer == _loadedTransfer) _loadedTransfer = 0;
                return;
            case NetWire.Bye:
                Out = "the host has stopped the race";
                Say(Out);
                Me = 0;
                return;
        }
    }

    // Why a guest cannot be let in: another build of KSA, or a mod the host's world may hold parts of that it does not have.
    private string Refusal(NetWire.Hello hello)
    {
        if (KsaBuild.Length > 0 && hello.KsaBuild.Length > 0 && hello.KsaBuild != KsaBuild)
        {
            return $"the host runs KSA {KsaBuild} and you run {hello.KsaBuild}: you each need the same build";
        }
        HashSet<string> theirs = [.. hello.Mods.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        string[] missing = [.. Mods.Where(m => !theirs.Contains(m)).Order()];
        return missing.Length > 0 ? $"the host's world needs mods you do not have enabled: {string.Join(", ", missing)}" : "";
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
        int n = NetWire.Write(message, _out);
        if (Impaired is { } line && line.Any)
        {
            if (_chance.NextDouble() < line.Loss)
            {
                Dropped++;
                return;
            }
            double due = _clock() + Math.Max(0.0, line.DelaySeconds + (line.JitterSeconds * ((2.0 * _chance.NextDouble()) - 1.0)));
            _late.Add((due, _out[..n], to));
            return;
        }
        Put(_out.AsSpan(0, n), to);
    }

    private void Put(ReadOnlySpan<byte> data, IPEndPoint to)
    {
        try
        {
            _socket.SendTo(data, SocketFlags.None, to);
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException or ArgumentException)
        {
        }
    }

    /// <summary>A bad line, made on purpose: how long each datagram this game sends is held back, how much that varies either way, and the share of them lost.</summary>
    public readonly record struct Line(double DelaySeconds, double JitterSeconds, double Loss)
    {
        public bool Any => DelaySeconds > 0.0 || JitterSeconds > 0.0 || Loss > 0.0;
    }

    /// <summary>
    /// For a developer: every datagram this game sends held back, jittered and lost as a line across
    /// the world would, so a race can be tried on one machine as it would be across the internet.
    /// Datagrams held back go out in <see cref="Update"/>, so no sooner than a frame.
    /// </summary>
    public Line? Impaired { get; set; }

    /// <summary>How many datagrams <see cref="Impaired"/> has lost on purpose.</summary>
    public int Dropped { get; private set; }

    private readonly List<(double Due, byte[] Data, IPEndPoint To)> _late = [];
    private readonly Random _chance = new();

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
