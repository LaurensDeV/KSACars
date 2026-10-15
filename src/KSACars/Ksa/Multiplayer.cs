using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// Racing other players, each in their own game: one hosts and the others join it with a code. Each
/// lays the same course where their own car is, and each car is sent to the others as where it is
/// on that course (<see cref="RacePose"/>), so it is put in the same place on every copy; in each
/// game the others' cars are copies of theirs, puppets the mod puts where they are told to be.
///
/// <para>Reaching a host takes no account and nothing installed. The host's port is opened on its
/// router through UPnP where the router allows it, and the address it is reached at is asked of a
/// public STUN server either way; where neither gets a guest in, the guest gives the host its own
/// code and the host lets it in, which opens the way through both routers from inside.</para>
///
/// <para>The panel asks; this runs once a frame from the frame hook. Never throws.</para>
/// </summary>
internal static class Multiplayer
{
    public const int DefaultPort = 50000;

    // States go out this often: a car at 80 m/s moves 2.7 m between two.
    private const double SendEverySeconds = 1.0 / 30.0;

    // The route a pose is measured along, which has to be the same on every copy whatever car is driven: the racing display's.
    private const double RouteTurnRadiusM = 8.0;

    // Further across the route than this a car is looked for along all of it again.
    private const double LostAcrossM = 40.0;

    // A car not heard from for this long is let go of where it is, and stands there.
    private const double LostSeconds = 3.0;

    // A port opened on the router is leased for this long, so a game that crashes leaves it to close.
    private const int LeaseSeconds = 4 * 3600;

    // The lights go out this long after the host presses Start.
    private const double CountdownSeconds = 5.0;

    private const int MessagesKept = 6;

    private sealed class Remote(string car)
    {
        public string Car { get; } = car;
        public Vehicle? Craft { get; set; }
        public bool Seated { get; set; }
        public int Frames { get; set; }
    }

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly Dictionary<byte, Remote> Remotes = [];
    private static readonly List<string> Said = [];

    private static NetSession? _session;
    private static CancellationTokenSource? _cancel;
    private static Task<Upnp.Opened>? _opening;
    private static Task<IPEndPoint?>? _finding;
    private static double _sendNext;
    private static int _index = -1, _routeGeneration = -1;
    private static uint _raceSeen;
    private static bool _held;
    private static string? _layAsked;
    private static string _hashOf = "", _hash = "";

    /// <summary>The name this player races under.</summary>
    public static string PlayerName { get; set; } = Environment.UserName;

    /// <summary>The port a host listens on.</summary>
    public static int Port { get; set; } = DefaultPort;

    public static bool Active => _session is not null;
    public static bool Hosting => _session is { Is: NetSession.Role.Host };
    public static bool Joined => _session is { Joined: true };

    /// <summary>What has happened lately, newest last, for the panel.</summary>
    public static IReadOnlyList<string> Recent => Said;

    /// <summary>Whether this game's copy of the course is the one the race is on.</summary>
    public static bool OnTheCourse { get; private set; }

    /// <summary>What is wrong with taking part, for the panel: no course, the wrong course, or nothing.</summary>
    public static string Trouble { get; private set; } = "";

    public static IEnumerable<NetSession.Racer> Racers => _session?.Racers.OrderBy(r => r.Box) ?? Enumerable.Empty<NetSession.Racer>();

    public static byte Me => _session?.Me ?? 0;

    /// <summary>How long until the lights go out, s, while they have not.</summary>
    public static double? Countdown => _session?.Start is { } start && start.At > Now() ? start.At - Now() : null;

    /// <summary>The code a host gives its guests, the best that is known so far, or nothing yet; and what it is.</summary>
    public static (string Code, string How)? HostCode
    {
        get
        {
            if (_session is not { Is: NetSession.Role.Host } s) return null;
            if (_opening is { IsCompletedSuccessfully: true } opened && opened.Result.External is { } external && !JoinCode.IsPrivate(external))
            {
                return (JoinCode.Of(new IPEndPoint(external, s.Port)), "your router opened the port");
            }
            return s.Public is { } seen ? (JoinCode.Of(seen), "if it does not work, swap codes and let your guest in") : null;
        }
    }

    /// <summary>The code a guest gives the host to be let in by, once a STUN server has said where it is seen.</summary>
    public static string? GuestCode => _session is { Is: NetSession.Role.Client, Public: { } seen } ? JoinCode.Of(seen) : null;

    /// <summary>This machine's address and the port on its own network, for a guest in the same house.</summary>
    public static string? LocalAddress
    {
        get
        {
            if (_session is not { } s) return null;
            try { return $"{Upnp.LocalTowards(IPAddress.Parse("8.8.8.8"))}:{s.Port}"; }
            catch (SocketException) { return null; }
        }
    }

    /// <summary>What the router said about opening the port, while it is being asked and after.</summary>
    public static string RouterSays => _opening switch
    {
        null => "",
        { IsCompleted: false } => "asking the router to open the port...",
        { IsCompletedSuccessfully: true } t when t.Result.External is { } ip && JoinCode.IsPrivate(ip)
            => $"the router opened the port, but is itself behind another at {ip}: swap codes instead",
        { IsCompletedSuccessfully: true } t => t.Result.Why.Length > 0 ? t.Result.Why : $"the router opened port {_session?.Port}",
        _ => "the router could not be asked",
    };

    /// <summary>Whether a box of the grid is a racer's, so an opponent is not put in it.</summary>
    public static bool Holds(int box) => _session is { } s && s.Racers.Any(r => r.Box == box);

    private static double Now() => Clock.Elapsed.TotalSeconds;

    /// <summary>Hosts a race on <see cref="Port"/>.</summary>
    public static void Host()
    {
        Stop();
        try
        {
            NetSession s = new(NetSession.Role.Host, PlayerName, MyCar(), Port, Now);
            _session = s;
            _cancel = new CancellationTokenSource();
            _opening = Upnp.OpenAsync(s.Port, "Fast & Purrious race", LeaseSeconds, _cancel.Token);
            _ = s.AskWhereAsync(_cancel.Token);
            Say($"hosting on port {s.Port}");
        }
        catch (SocketException e)
        {
            Say(e.SocketErrorCode == SocketError.AddressAlreadyInUse ? $"port {Port} is in use: is another race running?" : $"could not host: {e.Message}");
            Stop(quiet: true);
        }
    }

    /// <summary>Gets ready to join: a socket of this game's own and its code, for a host that has to let it in.</summary>
    public static void Ready()
    {
        if (_session is { Is: NetSession.Role.Client }) return;
        Stop();
        try
        {
            NetSession s = new(NetSession.Role.Client, PlayerName, MyCar(), 0, Now);
            _session = s;
            _cancel = new CancellationTokenSource();
            _ = s.AskWhereAsync(_cancel.Token);
        }
        catch (SocketException e)
        {
            Say($"could not open a socket: {e.Message}");
            Stop(quiet: true);
        }
    }

    /// <summary>Joins the host at a code, or a host and port.</summary>
    public static void Join(string text)
    {
        if (JoinCode.Read(text, out string why) is not { } at)
        {
            Say(why);
            return;
        }
        Ready();
        if (_session is null) return;
        Say($"looking for {at.Host}:{at.Port}...");
        _finding = Find(at.Host, at.Port, _cancel?.Token ?? CancellationToken.None);
    }

    /// <summary>Has a host open the way through its router to a guest's code.</summary>
    public static void LetIn(string text)
    {
        if (_session is not { Is: NetSession.Role.Host } s) return;
        if (JoinCode.Read(text, out string why) is not { } at || !IPAddress.TryParse(at.Host, out IPAddress? ip))
        {
            Say(why.Length > 0 ? why : "a guest's code is needed here, not a name");
            return;
        }
        s.PunchTo(new IPEndPoint(ip, at.Port));
    }

    /// <summary>A host starts the race: every car to its box, and the lights out a few seconds on.</summary>
    public static void StartRace()
    {
        if (_session is not { Is: NetSession.Role.Host } s) return;
        s.StartRace(CountdownSeconds, (byte)Math.Clamp(RaceHud.Laps, 1, 255));
    }

    /// <summary>Leaves or ends the race, closes the port on the router, and takes the others' cars away.</summary>
    public static void Stop() => Stop(quiet: false);

    private static void Stop(bool quiet)
    {
        bool was = _session is not null;
        _cancel?.Cancel();
        _session?.Dispose();
        if (_opening is { IsCompletedSuccessfully: true } opened && opened.Result.Gateway is { } gateway && opened.Result.External is not null && _session is { } s)
        {
            _ = Upnp.CloseAsync(gateway, s.Port);
        }
        (_session, _cancel, _opening, _finding) = (null, null, null, null);
        foreach (Remote r in Remotes.Values) Remove(r);
        Remotes.Clear();
        ReleaseHold();
        (_raceSeen, _layAsked, OnTheCourse, Trouble, _index) = (0, null, false, "", -1);
        if (was && !quiet) Say("out of the race");
    }

    private static async Task<IPEndPoint?> Find(string host, int port, CancellationToken token)
    {
        if (IPAddress.TryParse(host, out IPAddress? ip)) return new IPEndPoint(ip.MapToIPv4(), port);
        IPAddress[] found = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, token);
        return found.Length > 0 ? new IPEndPoint(found[0], port) : null;
    }

    /// <summary>Once a frame, from the frame hook. Never throws.</summary>
    public static void Update()
    {
        if (_session is not { } s) return;
        try
        {
            if (_finding is { IsCompleted: true } finding)
            {
                _finding = null;
                if (finding.IsCompletedSuccessfully && finding.Result is { } host) s.Join(host);
                else Say("that host could not be found");
            }

            s.Update();
            foreach (string said in s.Said()) Say(said);

            Vehicle? flown = KsaWorld.ControlledVehicle is { IsDisposed: false } v ? v : null;
            Buggies.Entry? mine = flown is null ? null : Buggies.Of(flown);
            s.Set(Courses.Laid ?? "", HashOf(Courses.LaidShape), mine?.Drive.Profile.PartId ?? s.Car);

            Match();
            Route? route = OnTheCourse ? SharedRoute(out _) : null;
            if (route is null)
            {
                foreach (Remote r in Remotes.Values) Remove(r);
                Remotes.Clear();
                return;
            }
            if (mine is not null && flown is not null) Send(s, route, mine);
            Place(s, route, flown);
            Race(s, flown, mine);
        }
        catch (Exception e)
        {
            Say($"the race went wrong: {e.GetBaseException().Message}");
            Log.Error("multiplayer failed", e);
        }
    }

    // Whether this copy of the course is the race's; on a guest, lays it at the car if the library has the host's.
    private static void Match()
    {
        if (_session is not { } s) return;
        string mine = HashOf(Courses.LaidShape);
        if (s.Is == NetSession.Role.Host)
        {
            OnTheCourse = mine.Length > 0;
            Trouble = OnTheCourse ? "" : "lay a course for the race";
            return;
        }
        if (!s.Joined || s.CourseHash.Length == 0)
        {
            OnTheCourse = false;
            Trouble = s.Joined ? "the host has not laid a course" : "";
            return;
        }
        OnTheCourse = mine == s.CourseHash;
        if (OnTheCourse)
        {
            Trouble = "";
            return;
        }
        string theirs = s.Course;
        if (CircuitLibrary.Load(theirs, out _) is not { } library)
        {
            Trouble = $"the host is racing on {theirs}, which you do not have";
            return;
        }
        if (HashOf(library.ToJson()) != s.CourseHash)
        {
            Trouble = $"your {theirs} is not the host's: you each need the same course file";
            return;
        }
        if (_layAsked == s.CourseHash)
        {
            Trouble = $"laying {theirs}...";
            return;
        }
        _layAsked = s.CourseHash;
        Courses.Lay(theirs);
        Say($"laying {theirs} where your car is, to race on");
    }

    private static string HashOf(string? shape)
    {
        if (shape is null) return "";
        if (shape == _hashOf) return _hash;
        (_hashOf, _hash) = (shape, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(shape)))[..16]);
        return _hash;
    }

    private static Route? SharedRoute(out Celestial? body)
    {
        body = null;
        if (!Roads.Any) return null;
        if (_routeGeneration != Roads.Generation) (_routeGeneration, _index) = (Roads.Generation, -1);
        return Roads.RouteOver(null, 0.0, RouteTurnRadiusM, out body, out _, out _);
    }

    private static void Send(NetSession s, Route route, Buggies.Entry mine)
    {
        double now = Now();
        if (now < _sendNext || !mine.PlaceValid) return;
        _sendNext = Math.Max(_sendNext + SendEverySeconds, now);
        RacePose pose = RacePose.Of(route, ref _index, mine.PlaceCcf, mine.Body2Ccf, mine.VelocityCcf, mine.SpinBody, 10.0 + mine.SpeedMs);
        if (Math.Abs(pose.Offset.Y) > LostAcrossM)
        {
            _index = -1;
            pose = RacePose.Of(route, ref _index, mine.PlaceCcf, mine.Body2Ccf, mine.VelocityCcf, mine.SpinBody, 0.0);
        }
        DriveInput input = mine.Input;
        s.Send(pose, (float)input.Throttle, (float)input.Steer, (float)input.Brake, (byte)Math.Clamp(mine.Drive.Gear, 0, 255), mine.Beam != BeamSetting.Off);
    }

    // Each other racer's car where its game says it is, made the first time it is heard of.
    private static void Place(NetSession s, Route route, Vehicle? flown)
    {
        HashSet<byte> racing = [];
        bool madeOne = false;
        double now = Now();
        foreach (NetSession.Racer racer in s.Racers)
        {
            if (racer.Id == s.Me || racer.Track.At(now) is not { } at) continue;
            racing.Add(racer.Id);
            if (Remotes.TryGetValue(racer.Id, out Remote? remote) && (remote.Car != racer.Car || remote.Craft is { } gone && !KsaWorld.IsAlive(gone)))
            {
                Remove(remote);
                Remotes.Remove(racer.Id);
                remote = null;
            }
            (double3 positionCcf, doubleQuat body2Ccf, double3 velocityCcf) = at.Pose.On(route);
            if (remote is null)
            {
                // One a frame: each is a craft built and set down.
                if (madeOne || flown is null || SharedRoute(out Celestial? body) is null || body is null) continue;
                madeOne = true;
                Remotes[racer.Id] = remote = new Remote(racer.Car);
                if (!ModLibrary.TryGet(racer.Car, out PartTemplate? template) || template is null)
                {
                    Say($"{racer.Name} drives a {racer.Car}, which this game does not have");
                    continue;
                }
                double3 cce = positionCcf.Transform(body.GetCcf2Cce());
                remote.Craft = CraftSpawner.SpawnOf(template, flown, $"{racer.Name} (online)", body,
                                                    body.GetLatitudeFromCce(cce), body.GetLongitudeFromCce(cce));
                if (remote.Craft is null) Say($"{racer.Name}'s car could not be made");
                continue;
            }
            if (remote.Craft is not { } craft || Buggies.Of(craft) is null) continue;

            if (!remote.Seated && ++remote.Frames > 10)
            {
                // What the launch window's Fill Seats does, so there is a kitten at the wheel.
                List<IVASeat> seats = [.. craft.Parts.Modules.Get<IVASeat>()];
                if (seats.Count > 0) CrewAssignmentWindow.FillSeats([seats[0]], craft.Parts, craft.Id, hasLaunched: true);
                remote.Seated = true;
            }

            if (now - racer.Track.HeardAt > LostSeconds)
            {
                Buggies.Puppet(craft, null);
                Buggies.Hold(craft, new DriveInput(0.0, 0.0, Brake: 1.0), 0.5);
                continue;
            }
            Buggies.Puppet(craft, (positionCcf, body2Ccf, velocityCcf, at.Pose.SpinBody));
            Buggies.Hold(craft, new DriveInput(at.Inputs.Throttle, at.Inputs.Steer, at.Inputs.Brake), 0.25);
            if (Buggies.Of(craft) is { } puppet) puppet.Beam = at.Inputs.Lights ? BeamSetting.Low : BeamSetting.Off;
        }
        foreach (byte gone in Remotes.Keys.Where(id => !racing.Contains(id)).ToArray())
        {
            Remove(Remotes[gone]);
            Remotes.Remove(gone);
        }
    }

    // A race the host has started: this car to its box and held there until the lights go out.
    private static void Race(NetSession s, Vehicle? flown, Buggies.Entry? mine)
    {
        if (s.Start is not { } start || flown is null || mine is null) return;
        if (start.Race != _raceSeen)
        {
            _raceSeen = start.Race;
            BuggyProfile profile = mine.Drive.Profile;
            NetSession.Racer? me = s.Racers.FirstOrDefault(r => r.Id == s.Me);
            if (me is not null
                && Roads.RouteOver(null, 0.0, Autopilot.TurnRadius(profile), out _, out RoadSurface? surface, out _) is { } route && surface is not null)
            {
                (double along, double left) = Opponents.Box(route, me.Box);
                Laps.Place(flown, profile, route, surface, along, left);
            }
            Buggies.Hold(flown, new DriveInput(0.0, 0.0, Brake: 1.0), 1e6);
            _held = true;
            RaceHud.Laps = start.Laps;
            Say($"to the grid, box {(me?.Box ?? 0) + 1}: {start.Laps} laps");
        }
        if (_held && Now() >= start.At)
        {
            ReleaseHold();
            RaceHud.Restart();
            Say("lights out!");
        }
    }

    private static void ReleaseHold()
    {
        if (!_held) return;
        _held = false;
        if (KsaWorld.ControlledVehicle is { IsDisposed: false } flown) Buggies.Hold(flown, default, 0.0);
    }

    private static string MyCar() =>
        KsaWorld.ControlledVehicle is { IsDisposed: false } v && Buggies.Of(v) is { } e ? e.Drive.Profile.PartId : "";

    private static void Remove(Remote remote)
    {
        if (remote.Craft is not { } craft) return;
        Buggies.Puppet(craft, null);
        try
        {
            if (KsaWorld.IsAlive(craft)) Universe.DestroyVehicle(craft);
        }
        catch (Exception e)
        {
            Log.Warn($"an online racer's car could not be taken away: {e.GetBaseException().Message}");
        }
    }

    private static void Say(string what)
    {
        Log.Info($"online: {what}");
        Said.Add(what);
        if (Said.Count > MessagesKept) Said.RemoveAt(0);
    }
}
