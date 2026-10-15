using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// Racing other players, each in their own game, in one world: the host's. One hosts and the
/// others join it with a code; each guest is sent a save of the host's world with its own car
/// already in it, and the course exactly as it is laid, loads that, and from then on keeps the
/// host's universe time. Every racer's car is a craft in every copy of the world; each game drives
/// its own and puts the others where their games say they are (<see cref="RacePose"/>).
///
/// <para>Reaching a host takes no account and nothing installed. The host's port is opened on its
/// router through UPnP where the router allows it, and the address it is reached at is asked of a
/// public STUN server either way; where neither gets a guest in, the guest gives the host its own
/// code and the host lets it in, which opens the way through both routers from inside.</para>
///
/// <para>While anyone has joined, nobody warps or pauses: <see cref="WarpHook"/> holds every world to
/// the speed <see cref="HeldSpeed"/> gives. The host's world stands still while a guest is being
/// handed it, so the guest starts at the very moment the host's world was saved.</para>
///
/// <para>The panel asks; this runs once a frame from the frame hook. Never throws.</para>
/// </summary>
internal static class Multiplayer
{
    public const int DefaultPort = 50000;

    /// <summary>The save a host's world is handed over in, on the host and on the guest.</summary>
    public const string HandoverSave = "Online race";

    // States go out this often: a car at 80 m/s moves 2.7 m between two.
    private const double SendEverySeconds = 1.0 / 30.0;

    // The route a pose is measured along, which has to be the same on every copy whatever car is driven: the racing display's.
    private const double RouteTurnRadiusM = 8.0;

    // Further across or along the route than this from the place it was found at, a car is looked for along all of it again.
    private const double LostAcrossM = 40.0, LostAlongM = 5.0;

    // A car not heard from for this long of the world's time is let go of where it is, and stands there.
    private const double LostSeconds = 3.0;

    // A port opened on the router is leased for this long, so a game that crashes leaves it to close.
    private const int LeaseSeconds = 4 * 3600;

    // The lights go out this long after the host presses Start.
    private const double CountdownSeconds = 5.0;

    // A guest's car is made beside the host's this far to its left where there is no course to put it on.
    private const double BesideM = 8.0;

    // Frames a guest's new car is given to settle before the world is saved with it.
    private const int SettleFrames = 45;

    // How long a guest's world is given to load before it is given up on, s.
    private const double LoadWithinSeconds = 120.0;

    private const int MessagesKept = 6;

    // Where a host's handover of its world to one guest is.
    private enum Handing { Spawned, Settling, Sent }

    private sealed class Handover(NetSession.Racer racer, Vehicle craft)
    {
        public NetSession.Racer Racer { get; } = racer;
        public Vehicle Craft { get; } = craft;
        public Handing Stage { get; set; } = Handing.Spawned;
        public int Frames { get; set; }
        public double SentAt { get; set; }
    }

    // Where a guest is in taking the host's world on.
    private enum Taking { Waiting, Loading, Laying, Racing }

    private sealed class Remote
    {
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
    private static double _sendNext, _loadingSince;
    private static int _index = -1, _routeGeneration = -1;
    private static uint _raceSeen, _worldTaken;
    private static bool _held, _routerTold;
    private static Handover? _handover;
    private static Taking _taking;
    private static int _joinSeen;
    private static string _myCraft = "";
    private static (string Name, Circuit Circuit, string? Shape)? _course;

    /// <summary>The name this player races under.</summary>
    public static string PlayerName { get; set; } = Environment.UserName;

    /// <summary>For a developer, a bad line made on purpose: every datagram this game sends held back, jittered and lost so.</summary>
    public static NetSession.Line? Impaired
    {
        get => _impaired;
        set
        {
            _impaired = value is { Any: true } ? value : null;
            if (_session is { } s) s.Impaired = _impaired;
        }
    }

    private static NetSession.Line? _impaired;

    /// <summary>The port a host listens on.</summary>
    public static int Port { get; set; } = DefaultPort;

    public static bool Active => _session is not null;
    public static bool Hosting => _session is { Is: NetSession.Role.Host };
    public static bool Joined => _session is { Joined: true };

    /// <summary>Whether anyone else is in the race with this game, which is when the world's speed is the race's to hold.</summary>
    public static bool Together => _session is { Joined: true } s && s.Racers.Count > 1;

    /// <summary>What has happened lately, newest last, for the panel.</summary>
    public static IReadOnlyList<string> Recent => Said;

    /// <summary>Whether this game is on the race's course, in the race's world, and can race.</summary>
    public static bool OnTheCourse { get; private set; }

    /// <summary>What is in the way of racing, for the panel, or nothing.</summary>
    public static string Trouble { get; private set; } = "";

    public static IEnumerable<NetSession.Racer> Racers => _session?.Racers.OrderBy(r => r.Box) ?? Enumerable.Empty<NetSession.Racer>();

    public static byte Me => _session?.Me ?? 0;

    /// <summary>How long until the lights go out, s, while they have not.</summary>
    public static double? Countdown => _session?.Start is { } start && start.At > Now() ? start.At - Now() : null;

    /// <summary>On a guest, how far its universe time is behind the host's, s, once both are known.</summary>
    public static double? Behind { get; private set; }

    /// <summary>
    /// The speed every world is held to while racers are together, or null where this game's own is
    /// its player's: the host's 1, or 0 while it hands a guest its world; a guest's what keeps it on
    /// the host's time. Read by <see cref="WarpHook"/> inside KSA's own calls.
    /// </summary>
    public static double? HeldSpeed { get; private set; }

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

    /// <summary>On a guest, where taking the host's world on has got to, for the panel; nothing once it is racing.</summary>
    public static string WorldSays => _session is { Is: NetSession.Role.Client, Joined: true } s
        ? _taking switch
        {
            Taking.Waiting => s.WorldShare is { } share ? $"receiving the host's world: {share * 100:F0}%" : "waiting for the host's world...",
            Taking.Loading => "loading the host's world...",
            Taking.Laying => "laying the host's course...",
            _ => "",
        }
        : "";

    /// <summary>
    /// Whether a craft is another racer's car, which nobody in this game may take control of, board
    /// or pick up: one the roster names for another racer, or the one a host is making for a guest.
    /// </summary>
    public static bool IsOthers(Vehicle? craft)
    {
        if (craft is null || _session is not { Joined: true } s) return false;
        // Loading the host's world hands control to the craft its save was flying, the host's; this game's own is taken a moment later.
        if (s.Is == NetSession.Role.Client && _taking == Taking.Loading) return false;
        if (_handover is { } h && ReferenceEquals(h.Craft, craft)) return true;
        string id = craft.Id;
        return s.Racers.Any(r => r.Id != s.Me && r.Craft == id);
    }

    /// <summary>Whether a box of the grid is a racer's, so an opponent is not put in it.</summary>
    public static bool Holds(int box) => _session is { } s && s.Racers.Any(r => r.Box == box);

    private static double Now() => Clock.Elapsed.TotalSeconds;

    /// <summary>Hosts a race on <see cref="Port"/>.</summary>
    public static void Host()
    {
        Stop();
        try
        {
            NetSession s = new(NetSession.Role.Host, PlayerName, MyCar(), Port, Now) { KsaBuild = Build.KsaRunning ?? "", Mods = EnabledMods() };
            s.UniverseClock = () => (Universe.GetElapsedSeconds(), Universe.SimulationSpeed);
            s.TrackClock = Universe.GetElapsedSeconds;
            s.Impaired = _impaired;
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
            NetSession s = new(NetSession.Role.Client, PlayerName, MyCar(), 0, Now) { KsaBuild = Build.KsaRunning ?? "", Mods = EnabledMods() };
            s.TrackClock = Universe.GetElapsedSeconds;
            s.Impaired = _impaired;
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

    /// <summary>Joins the host at a code, or a host and port: this game's world is then the host's.</summary>
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

    /// <summary>Leaves or ends the race, closes the port on the router, and lets the world run as its player has it again.</summary>
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
        // Whoever leaves the race is left with their own car alone: the others' are taken out of their world.
        if (_session is { } ending) ClearOthers(ending);
        if (_handover is { } pending) Destroy(pending.Craft);
        (_session, _cancel, _opening, _finding, _handover, _course) = (null, null, null, null, null, null);
        ReleaseHold();
        HeldSpeed = null;
        Behind = null;
        if (was) Universe.SetSimulationSpeed(1.0, alert: false);
        (_raceSeen, _worldTaken, _taking, OnTheCourse, Trouble, _index, _routerTold) = (0, 0, Taking.Waiting, false, "", -1, false);
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
            if (!_routerTold && _opening is { IsCompleted: true })
            {
                _routerTold = true;
                Say($"router: {RouterSays}{(HostCode is { } code ? $"; code {code.Code}" : "")}");
            }

            // Taken by a route the hook does not see: this game's own car is handed back.
            if (IsOthers(KsaWorld.ControlledVehicle) && KsaWorld.Vehicles.FirstOrDefault(c => c.Id == OwnCraft(s)) is { } own)
            {
                KsaWorld.GoTo(own);
                Say("that is another racer's car; you are back in your own");
            }
            Vehicle? flown = KsaWorld.ControlledVehicle is { IsDisposed: false } v ? v : null;
            Buggies.Entry? mine = flown is null ? null : Buggies.Of(flown);
            s.Set(Courses.Laid ?? "", "", mine?.Drive.Profile.PartId ?? s.Car);

            if (s.Is == NetSession.Role.Host) Hand(s, flown);
            else Take(s);
            HoldTheClock(s);

            Route? route = OnTheCourse ? SharedRoute(out _) : null;
            if (route is null) return;
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

    // The host: its own car's Id told to everyone, and each guest let in given a car in this world and then the world.
    private static void Hand(NetSession s, Vehicle? flown)
    {
        if (flown is not null) s.SetCraft(NetSession.HostId, flown.Id);
        OnTheCourse = Roads.Any;
        Trouble = OnTheCourse ? "" : "lay a course for the race";

        if (_handover is null && s.NextArrival() is { } racer)
        {
            if (flown is null || flown.Parent is not Celestial body)
            {
                Say($"{racer.Name} cannot be let in: no car is being flown");
                return;
            }
            if (Make(racer, flown, body) is not { } craft)
            {
                Say($"{racer.Name}'s car could not be made");
                return;
            }
            _handover = new Handover(racer, craft);
            Say($"making {racer.Name}'s car");
        }
        if (_handover is not { } h) return;
        if (!s.Racers.Contains(h.Racer) || !KsaWorld.IsAlive(h.Craft))
        {
            // Gone before it raced: the car made for it goes too.
            if (KsaWorld.IsAlive(h.Craft)) Destroy(h.Craft);
            _handover = null;
            return;
        }
        if (h.Stage == Handing.Sent && Now() - h.SentAt > LoadWithinSeconds)
        {
            s.Drop(h.Racer, $"it did not load the world in {LoadWithinSeconds:F0} s");
            Destroy(h.Craft);
            _handover = null;
            return;
        }

        h.Frames++;
        switch (h.Stage)
        {
            case Handing.Spawned when Buggies.Of(h.Craft) is { } entry && h.Frames > 5:
                // What the launch window's Fill Seats does, so there is a kitten at the wheel; and the car in its box.
                List<IVASeat> seats = [.. h.Craft.Parts.Modules.Get<IVASeat>()];
                if (seats.Count > 0) CrewAssignmentWindow.FillSeats([seats[0]], h.Craft.Parts, h.Craft.Id, hasLaunched: true);
                if (Roads.RouteOver(null, 0.0, Autopilot.TurnRadius(entry.Drive.Profile), out _, out RoadSurface? surface, out _) is { } route && surface is not null)
                {
                    (double along, double left) = Opponents.Box(route, h.Racer.Box);
                    Laps.Place(h.Craft, entry.Drive.Profile, route, surface, along, left);
                }
                s.SetCraft(h.Racer.Id, h.Craft.Id);
                (h.Stage, h.Frames) = (Handing.Settling, 0);
                return;
            case Handing.Settling when h.Frames >= SettleFrames:
                // Held still from here, so the guest starts at the moment this world was saved.
                if (!(HeldSpeed is 0.0 && KsaWorld.IsPaused))
                {
                    HeldSpeed = 0.0;
                    Universe.SetSimulationSpeed(0.0, alert: false);
                    return;
                }
                s.SendWorld(h.Racer, Pack());
                (h.Stage, h.Frames, h.SentAt) = (Handing.Sent, 0, Now());
                return;
            case Handing.Sent when h.Racer.HasWorld:
                _handover = null;
                return;
        }
    }

    // A car for a guest in this world, in its box if there is a course and beside the flown car if not.
    private static Vehicle? Make(NetSession.Racer racer, Vehicle flown, Celestial body)
    {
        if ((TemplateOf(racer.Car) ?? flown.Parts.Root.Template) is not { } template) return null;
        HashSet<string> taken = [.. KsaWorld.Vehicles.Select(v => v.Id)];
        string id = $"{racer.Name} (online)";
        for (int k = 2; taken.Contains(id); k++) id = $"{racer.Name} (online {k})";

        double3 at;
        if (Roads.RouteOver(null, 0.0, RouteTurnRadiusM, out _, out _, out _) is { } route)
        {
            (double along, double left) = Opponents.Box(route, racer.Box);
            (double3 on, double3 up, double3 ahead) = route.Standing(along);
            at = on + (Vec.Unit(Vec.Cross(up, ahead)) * left);
        }
        else if (Buggies.Of(flown) is { PlaceValid: true } mine)
        {
            double3 up = Vec.Unit(mine.PlaceCcf);
            at = mine.PlaceCcf + (Vec.Unit(Vec.Cross(up, mine.AheadCcf)) * BesideM);
        }
        else return null;
        double3 cce = at.Transform(body.GetCcf2Cce());
        return CraftSpawner.SpawnOf(template, flown, id, body, body.GetLatitudeFromCce(cce), body.GetLongitudeFromCce(cce));
    }

    // This world saved, and packed with the course as it is laid: what a guest is sent.
    private static byte[] Pack()
    {
        GameSaves.MakeUncompressedSave(HandoverSave);
        string folder = Path.Combine(GameSaves.SaveFolderPath, HandoverSave);
        List<(string, byte[])> files = [];
        foreach (string path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            files.Add(("save/" + Path.GetRelativePath(folder, path).Replace('\\', '/'), File.ReadAllBytes(path)));
        }
        if (Roads.Any && KsaWorld.ControlledVehicle?.Parent is Celestial body && Roads.CircuitOn(body) is { } circuit)
        {
            string course = JsonSerializer.Serialize(new Dictionary<string, string?>
            {
                ["name"] = Courses.Laid ?? "course", ["shape"] = Courses.LaidShape, ["circuit"] = circuit.ToJson(),
            });
            files.Add(("course.json", Encoding.UTF8.GetBytes(course)));
        }
        return WorldTransfer.Pack(files);
    }

    // A guest: the host's world, once it has all come, written as a save and loaded; then its own car flown and the course laid.
    private static void Take(NetSession s)
    {
        if (s.Joins != _joinSeen)
        {
            (_joinSeen, _taking, _worldTaken, _course, _raceSeen) = (s.Joins, Taking.Waiting, 0, null, 0);
        }
        if (!s.Joined)
        {
            // Out of the race, the host gone or stopped: this world keeps its own car alone, as the host's does.
            if (_taking != Taking.Waiting) ClearOthers(s);
            _taking = Taking.Waiting;
            OnTheCourse = false;
            Trouble = s.Out;
            return;
        }
        switch (_taking)
        {
            case Taking.Waiting when s.World is { } world && world.Transfer != _worldTaken:
                _worldTaken = world.Transfer;
                Load(world.Data);
                (_taking, _loadingSince) = (Taking.Loading, Now());
                break;
            case Taking.Loading:
                string mine = s.Racers.FirstOrDefault(r => r.Id == s.Me)?.Craft ?? "";
                if (Now() - _loadingSince > LoadWithinSeconds)
                {
                    Say("the host's world did not load; leaving the race");
                    Stop(quiet: true);
                    return;
                }
                if (!KsaWorld.InFlight || mine.Length == 0 || KsaWorld.Vehicles.FirstOrDefault(v => v.Id == mine) is not { } craft) break;
                KsaWorld.GoTo(craft);
                _myCraft = craft.Id;
                if (_course is { } course) Courses.LayAsIs(course.Name, course.Circuit, course.Shape);
                _taking = Taking.Laying;
                break;
            case Taking.Laying when !Courses.Busy && (Roads.Any || _course is null):
                s.SayLoaded();
                // The world, and with it the universe time every car is drawn on, has been replaced.
                s.ForgetTracks();
                _taking = Taking.Racing;
                Say("in the host's world, ready to race");
                break;
        }
        OnTheCourse = _taking == Taking.Racing && Roads.Any;
        Trouble = _taking == Taking.Racing && !Roads.Any ? "the host has not laid a course" : "";
    }

    // The host's world written into this game's saves as the handover save, and loaded in place of this one.
    private static void Load(byte[] packed)
    {
        string folder = Path.Combine(GameSaves.SaveFolderPath, HandoverSave);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        _course = null;
        foreach ((string name, byte[] data) in WorldTransfer.Unpack(packed))
        {
            if (name.StartsWith("save/", StringComparison.Ordinal))
            {
                string path = Path.Combine(folder, name["save/".Length..]);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, data);
            }
            else if (name == "course.json" && JsonSerializer.Deserialize<Dictionary<string, string?>>(data) is { } course
                     && course.GetValueOrDefault("circuit") is { } json && Circuit.FromJson(json, out _, out _) is { } circuit)
            {
                _course = (course.GetValueOrDefault("name") ?? "course", circuit, course.GetValueOrDefault("shape"));
            }
        }
        GameSaves.Refresh();
        GameSaves.LoadSaveGame(HandoverSave);
        Say("loading the host's world");
    }

    // Every world's speed while racers are together: the host's its own, a guest's run to keep the host's universe time.
    private static void HoldTheClock(NetSession s)
    {
        if (!Together)
        {
            // The race let go of the world: it runs again as if nobody had held it.
            bool wasHeld = HeldSpeed is not null;
            (HeldSpeed, Behind) = (null, null);
            if (wasHeld) Universe.SetSimulationSpeed(1.0, alert: false);
            return;
        }
        if (Universe.IsAutoWarpActive) Universe.AutoWarpStop(resetSimulationSpeed: false);

        if (s.Is == NetSession.Role.Host)
        {
            bool waiting = _handover is { Stage: Handing.Sent } || (_handover is null && s.Racers.Any(r => r.Id != NetSession.HostId && r.Craft.Length > 0 && !r.HasWorld));
            HeldSpeed = waiting || (_handover is { Stage: Handing.Settling } && HeldSpeed is 0.0) ? 0.0 : 1.0;
        }
        else if (_taking is Taking.Racing or Taking.Laying && s.HostUniverse is { } host)
        {
            double behind = host.Universe - Universe.GetElapsedSeconds();
            Behind = behind;
            HeldSpeed = ClockLock.Speed(behind, host.Speed);
        }
        else
        {
            // Loading a world, or waiting for one: KSA's own load is left to run as it does.
            HeldSpeed = _taking == Taking.Loading ? null : 1.0;
        }
        if (HeldSpeed is { } held && Math.Abs(Universe.SimulationSpeed - held) > 1e-9) Universe.SetSimulationSpeed(held, alert: false);
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
        // Moved along the route or far across it, as by being stood in a box, the car is looked for along all of it again.
        if (Math.Abs(pose.Offset.Y) > LostAcrossM || Math.Abs(pose.Offset.X) > LostAlongM)
        {
            _index = -1;
            pose = RacePose.Of(route, ref _index, mine.PlaceCcf, mine.Body2Ccf, mine.VelocityCcf, mine.SpinBody, 0.0);
        }
        DriveInput input = mine.Input;
        s.Send(pose, (float)input.Throttle, (float)input.Steer, (float)input.Brake, (byte)Math.Clamp(mine.Drive.Gear, 0, 255), mine.Beam != BeamSetting.Off,
               Universe.GetElapsedSeconds());
    }

    // Each other racer's car, the craft of its Id in this world, where its game says it is. A racer who joined after this
    // game took the world on has no car in it yet, and one is made with the Id it has in every other.
    private static void Place(NetSession s, Route route, Vehicle? flown)
    {
        HashSet<byte> racing = [];
        bool madeOne = false;
        double now = Universe.GetElapsedSeconds();
        foreach (NetSession.Racer racer in s.Racers)
        {
            racing.Add(racer.Id);
            if (racer.Id == s.Me || racer.Craft.Length == 0 || racer.Track.At(now) is not { } at) continue;
            if (!Remotes.TryGetValue(racer.Id, out Remote? remote)) Remotes[racer.Id] = remote = new Remote();
            (double3 positionCcf, doubleQuat body2Ccf, double3 velocityCcf) = at.Pose.On(route);

            if (remote.Craft is not { IsDisposed: false } || remote.Craft.Id != racer.Craft)
            {
                remote.Craft = KsaWorld.Vehicles.FirstOrDefault(v => v.Id == racer.Craft);
                remote.Seated = remote.Craft is not null;
            }
            if (remote.Craft is null)
            {
                // One a frame: each is a craft built and set down.
                if (madeOne || flown is null || SharedRoute(out Celestial? body) is null || body is null) continue;
                madeOne = true;
                if (TemplateOf(racer.Car) is not { } template)
                {
                    Say($"{racer.Name} drives a {racer.Car}, which this game does not have");
                    continue;
                }
                double3 cce = positionCcf.Transform(body.GetCcf2Cce());
                remote.Craft = CraftSpawner.SpawnOf(template, flown, racer.Craft, body, body.GetLatitudeFromCce(cce), body.GetLongitudeFromCce(cce));
                remote.Seated = false;
                continue;
            }
            Vehicle craft = remote.Craft;
            if (Buggies.Of(craft) is null) continue;

            if (!remote.Seated && ++remote.Frames > 10)
            {
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
        // A racer who has left: its car is taken out of every world alike, and is not left on the road.
        foreach (byte gone in Remotes.Keys.Where(id => !racing.Contains(id)).ToArray())
        {
            if (Remotes[gone].Craft is { } craft) Destroy(craft);
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

    private static string OwnCraft(NetSession s) =>
        s.Racers.FirstOrDefault(r => r.Id == s.Me)?.Craft is { Length: > 0 } craft ? craft : _myCraft;

    // Every other racer's car taken out of this world: those puppeted, and any the roster names that never were.
    private static void ClearOthers(NetSession s)
    {
        HashSet<string> others = [.. s.Racers.Where(r => r.Id != s.Me && r.Craft.Length > 0).Select(r => r.Craft)];
        foreach (Remote r in Remotes.Values) if (r.Craft is { } craft) others.Add(craft.Id);
        // Once out of the race a guest's number is gone, so its own car is the one it was put in.
        string mine = s.Is == NetSession.Role.Host ? s.Racers.FirstOrDefault(r => r.Id == NetSession.HostId)?.Craft ?? "" : _myCraft;
        others.Remove(mine);
        foreach (Vehicle craft in KsaWorld.Vehicles.Where(v => others.Contains(v.Id)).ToArray()) Destroy(craft);
        Remotes.Clear();
    }

    private static void Destroy(Vehicle craft)
    {
        Buggies.Puppet(craft, null);
        try
        {
            if (KsaWorld.IsAlive(craft) && !ReferenceEquals(craft, KsaWorld.ControlledVehicle)) Universe.DestroyVehicle(craft);
        }
        catch (Exception e)
        {
            Log.Warn($"a car that left the race could not be taken away: {e.GetBaseException().Message}");
        }
    }

    // KSA's TryGet has no branch for a part template and always answers no; Get has one, and throws for an id it has not loaded.
    private static PartTemplate? TemplateOf(string id)
    {
        if (id.Length == 0) return null;
        try { return ModLibrary.Get<PartTemplate>(id); }
        catch (NullReferenceException) { return null; }
    }

    private static string[] EnabledMods() =>
        ModLibrary.Manifest is { } manifest ? [.. manifest.Mods.Where(m => m.Enabled).Select(m => m.Id)] : [];

    private static string MyCar() =>
        KsaWorld.ControlledVehicle is { IsDisposed: false } v && Buggies.Of(v) is { } e ? e.Drive.Profile.PartId : "";

    /// <summary>Where the race is, for the bridge: who is in it, the clocks, and for each other racer how far its car is from where its game says.</summary>
    public static Dictionary<string, object?> Report()
    {
        Dictionary<string, object?> r = new()
        {
            ["active"] = Active, ["hosting"] = Hosting, ["joined"] = Joined, ["me"] = (int)Me, ["port"] = _session?.Port,
            ["code"] = HostCode?.Code, ["guest_code"] = GuestCode, ["local"] = LocalAddress, ["router"] = RouterSays,
            ["public"] = _session?.Public?.ToString(), ["host_offset_s"] = _session is { } s ? Math.Round(s.HostOffset, 4) : null,
            ["on_the_course"] = OnTheCourse, ["trouble"] = Trouble, ["world"] = WorldSays, ["countdown_s"] = Countdown is { } c ? Math.Round(c, 2) : null,
            ["held"] = _held, ["held_speed"] = HeldSpeed, ["speed"] = Universe.SimulationSpeed, ["universe_s"] = Math.Round(Universe.GetElapsedSeconds(), 3),
            ["behind_s"] = Behind is { } b ? Math.Round(b, 4) : null, ["recent"] = Said.ToArray(),
            ["line"] = _impaired is { } line ? $"{line.DelaySeconds * 1000:F0} ms, +-{line.JitterSeconds * 1000:F0} ms, {line.Loss * 100:F0}% lost" : null,
            ["dropped"] = _session?.Dropped,
        };
        List<Dictionary<string, object?>> racers = [];
        Route? route = OnTheCourse ? SharedRoute(out _) : null;
        double now = Universe.GetElapsedSeconds();
        foreach (NetSession.Racer racer in Racers)
        {
            Dictionary<string, object?> one = new()
            {
                ["id"] = (int)racer.Id, ["name"] = racer.Name, ["car"] = racer.Car, ["box"] = (int)racer.Box, ["craft"] = racer.Craft,
                ["has_world"] = racer.HasWorld,
                ["heard_s_ago"] = double.IsFinite(racer.Track.HeardAt) ? Math.Round(now - racer.Track.HeardAt, 2) : null,
                ["s_m"] = racer.Track.Newest is { } newest ? Math.Round(newest.Pose.S, 1) : null,
            };
            if (Remotes.TryGetValue(racer.Id, out Remote? remote) && remote.Craft is { } craft && Buggies.Of(craft) is { PlaceValid: true } e)
            {
                one["puppet"] = KsaWorld.DisplayName(craft);
                if (route is not null && racer.Track.At(now) is { } at)
                {
                    (double3 wanted, _, _) = at.Pose.On(route);
                    one["off_by_m"] = Math.Round(Vec.Len(e.PlaceCcf - wanted), 3);
                    one["speed_ms"] = Math.Round(e.SpeedMs, 1);
                }
            }
            racers.Add(one);
        }
        r["racers"] = racers;
        return r;
    }

    private static void Say(string what)
    {
        Log.Info($"online: {what}");
        Said.Add(what);
        if (Said.Count > MessagesKept) Said.RemoveAt(0);
    }
}
