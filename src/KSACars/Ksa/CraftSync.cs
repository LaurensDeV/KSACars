using KSA;
using StarMap.API.Events;
using StarMap.API.Services;

namespace KSACars;

/// <summary>
/// A car in a world that is shared, by whatever is sharing it.
///
/// <para>Some other mod may be keeping this game's craft and another game's in one world. This
/// mod does not know which, or whether: it asks on StarMap's event bus whether craft are shared
/// here, says what a car is doing beyond what KSA knows of a craft, and is told the same of the
/// cars being driven elsewhere. With nobody answering, none of this does anything.</para>
///
/// <para>A bus event is matched by its name and by the type it carries, and a type of this mod's
/// own is another type in every other mod. So every message is a dictionary of names to plain
/// values and KSA's own, and what a car says of itself is bytes this mod alone reads:</para>
///
/// <list type="bullet">
/// <item><c>craftsync.hello</c>, said once the mods are loaded, with <c>mod</c>. Whoever shares
/// craft sets <c>provider</c> true in the same dictionary, and <c>session</c> to whether a world
/// is being shared now.</item>
/// <item><c>craftsync.session</c>, heard: <c>active</c>.</item>
/// <item><c>craftsync.authority</c>, heard: <c>craft</c> and <c>remote</c>. A remote car is put
/// where another game has it at every physics step, by whoever shares it; this mod goes on
/// turning its wheels and working its springs, and moves nothing.</item>
/// <item><c>craftsync.state</c>, said every frame of the car this player drives: <c>craft</c>,
/// <c>channel</c> and <c>data</c>.</item>
/// <item><c>craftsync.state.remote</c>, heard: the same, as another game said it of a car that
/// is remote here.</item>
/// </list>
/// </summary>
internal static class CraftSync
{
    private const string Hello = "craftsync.hello", Session = "craftsync.session", Authority = "craftsync.authority",
                         State = "craftsync.state", RemoteState = "craftsync.state.remote";

    // What a car says of itself, and which way of saying it this is.
    private const string Channel = "ksacars:drive/v1";
    private const byte Layout = 1;
    private const int Bytes = 7;

    // How long a remote car goes on being driven as it was last said to be, with nothing new heard.
    private const double HeldSeconds = 0.5;

    private static IEventPublisher<Dictionary<string, object?>>? _hello, _state;
    private static readonly List<IDisposable> Listening = [];
    private static readonly HashSet<Vehicle> Remote = [];

    /// <summary>Whether something in this game shares craft with other games at all.</summary>
    public static bool Provided { get; private set; }

    /// <summary>Whether this game is in a shared world now.</summary>
    public static bool InSession { get; private set; }

    /// <summary>Whether a car is being driven in another game, and is only drawn in this one.</summary>
    public static bool IsRemote(Vehicle? craft) => craft is not null && Remote.Count > 0 && Remote.Contains(craft);

    /// <summary>Listens. From the mod's constructor: nothing said on the bus is kept for whoever listens later.</summary>
    public static void Start(IEventBus bus)
    {
        try
        {
            Listening.Add(bus.Subscribe<Dictionary<string, object?>>(OnSession, Session));
            Listening.Add(bus.Subscribe<Dictionary<string, object?>>(OnAuthority, Authority));
            Listening.Add(bus.Subscribe<Dictionary<string, object?>>(OnRemoteState, RemoteState));
            _hello = bus.GetPublisher<Dictionary<string, object?>>(Hello);
            _state = bus.GetPublisher<Dictionary<string, object?>>(State);
        }
        catch (Exception e)
        {
            Log.Warn($"cars will not be shared with other games: {e.GetBaseException().Message}");
        }
    }

    /// <summary>Asks whether craft are shared here. Once every mod is loaded, so whoever would answer is listening.</summary>
    public static void Ask()
    {
        if (_hello is null) return;
        try
        {
            Dictionary<string, object?> asked = new() { ["mod"] = "KSACars" };
            _hello.Publish(asked);
            Provided = asked.GetValueOrDefault("provider") is true;
            InSession = asked.GetValueOrDefault("session") is true;
            Log.Info(Provided
                ? "another mod shares craft between games: cars are shared through it, and this mod's own online race is put away"
                : "nothing shares craft between games here");
        }
        catch (Exception e)
        {
            Log.Warn($"could not ask whether craft are shared: {e.GetBaseException().Message}");
        }
    }

    private static void OnSession(Dictionary<string, object?> said)
    {
        InSession = said.GetValueOrDefault("active") is true;
        if (!InSession) Remote.Clear();
    }

    private static void OnAuthority(Dictionary<string, object?> said)
    {
        if (said.GetValueOrDefault("craft") is not Vehicle craft) return;
        if (said.GetValueOrDefault("remote") is true) Remote.Add(craft);
        else if (Remote.Remove(craft)) Buggies.Hold(craft, default, 0.0);
    }

    private static void OnRemoteState(Dictionary<string, object?> said)
    {
        if (said.GetValueOrDefault("channel") is not string channel || channel != Channel) return;
        if (said.GetValueOrDefault("craft") is not Vehicle craft || said.GetValueOrDefault("data") is not byte[] data) return;
        if (data.Length < Bytes || data[0] != Layout || Buggies.Of(craft) is not { } car) return;
        DriveInput input = new((sbyte)data[1] / 127.0, (sbyte)data[2] / 127.0, data[3] / 255.0);
        Buggies.Hold(craft, input, HeldSeconds, turn: (sbyte)data[4] / 127.0, boost: (data[5] & 1) != 0);
        car.Downforce = (data[5] & 2) != 0;
        car.Throttle = data[6] / 255.0;
    }

    /// <summary>Says what the car this player drives is doing, once a frame while a world is shared.</summary>
    public static void Frame()
    {
        if (!InSession || _state is null) return;
        if (KsaWorld.ControlledVehicle is not { } craft || IsRemote(craft) || Buggies.Of(craft) is not { } car) return;
        byte flags = (byte)((car.HatchWanted[(int)ThrusterGroup.Boost] ? 1 : 0) | (car.Downforce ? 2 : 0));
        byte[] data =
        [
            Layout,
            (byte)(sbyte)Math.Round(Math.Clamp(car.Input.Throttle, -1.0, 1.0) * 127.0),
            (byte)(sbyte)Math.Round(Math.Clamp(car.Input.Steer, -1.0, 1.0) * 127.0),
            (byte)Math.Round(Math.Clamp(car.Input.Brake, 0.0, 1.0) * 255.0),
            (byte)(sbyte)Math.Round(Math.Clamp(car.Turn, -1.0, 1.0) * 127.0),
            flags,
            (byte)Math.Round(Math.Clamp(car.Throttle, 0.0, 1.0) * 255.0),
        ];
        try
        {
            _state.Publish(new() { ["craft"] = craft, ["channel"] = Channel, ["data"] = data });
        }
        catch (Exception e)
        {
            Log.Warn($"could not say what {KsaWorld.DisplayName(craft)} is doing: {e.GetBaseException().Message}");
            _state = null;
        }
    }
}
