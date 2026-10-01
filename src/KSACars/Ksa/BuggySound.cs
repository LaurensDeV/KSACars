using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// A car's engine: an idle loop and an under-load loop, crossfaded by throttle and each re-pitched
/// every frame to the engine's RPM, from the sounds its profile names.
///
/// <para>Two layers rather than one pitched sample, because an idle raised three times over is still an
/// idle, only faster; a loaded engine sounds different, not just higher. The engine runs while the
/// buggy is the craft being flown, and a parked one is silent, as is any under warp.</para>
///
/// <para>Pitch and volume are set as plain multipliers every frame. The engine re-applies both on every
/// active channel each frame, and only its <c>Param</c>-driven multipliers are cleared afterwards, so a
/// value written here stays until it is written again.</para>
/// </summary>
internal sealed class BuggySound
{

    // Past this the engine is silent. Under warp its RPM swings many times a second and the loops are
    // re-pitched to match, which is noise rather than an engine.
    private const double LoudestSimSpeed = 4.0;

    // The starter clip's length, less its fade: the loops come in as it catches.
    private const long CatchesAfterMs = 2000;

    private sealed class Engine
    {
        public required BuggyProfile Profile;
        public long StartedMs;
        public IChannel? Idle;
        public IChannel? Load;
    }

    private readonly Dictionary<Vehicle, Engine> _running = [];
    private readonly List<Vehicle> _stopped = [];
    private readonly HashSet<string> _warned = [];

    public void Update(Buggies buggies, float volume)
    {
        Vehicle? flown = KsaWorld.ControlledVehicle;
        Camera? camera = SoundChannels.Listener();

        foreach (Buggies.Entry e in buggies.All)
        {
            bool on = ReferenceEquals(e.Craft, flown) && camera is not null && volume > 0f;
            if (!on)
            {
                Stop(e.Craft, camera);
                continue;
            }

            if (!TrySpatial(camera!, e.Craft, out SpatialAudio spatial)) continue;

            if (!_running.TryGetValue(e.Craft, out Engine? engine))
            {
                engine = new Engine { Profile = e.Drive.Profile, StartedMs = Environment.TickCount64 };
                _running[e.Craft] = engine;
                PlayOnce(engine.Profile.SoundPrefix + "Start", spatial, volume);
            }
            if (Environment.TickCount64 - engine.StartedMs < CatchesAfterMs) continue;

            // Cut rather than stopped: the engine is still running, so no key-off going in and no
            // starter coming out.
            if (KsaWorld.SimulationSpeed > LoudestSimSpeed)
            {
                Cut(engine);
                continue;
            }

            engine.Idle = Keep(engine.Idle, engine.Profile.SoundPrefix + "Idle", spatial, volume);
            engine.Load = Keep(engine.Load, engine.Profile.SoundPrefix + "Load", spatial, volume);
            Mix(engine, e.Drive, volume);
        }

        foreach (Vehicle v in _running.Keys)
        {
            if (!buggies.All.Any(b => ReferenceEquals(b.Craft, v))) _stopped.Add(v);
        }
        foreach (Vehicle v in _stopped) Stop(v, camera);
        _stopped.Clear();
    }

    public void StopAll()
    {
        foreach (Engine engine in _running.Values) Cut(engine);
        _running.Clear();
    }

    private static void Mix(Engine engine, BuggyDrive drive, float volume)
    {
        BuggyProfile p = drive.Profile;
        double rev = Math.Clamp((drive.Rpm - p.IdleRpm) / (p.RedlineRpm - p.IdleRpm), 0.0, 1.0);
        double loaded = Math.Clamp((rev * 1.4) + (0.35 * drive.Load), 0.0, 1.0);

        Set(engine.Idle, drive.Rpm / (p.IdleRecordedRpm > 0.0 ? p.IdleRecordedRpm : p.IdleRpm), (1.0 - (0.85 * loaded)) * volume);
        Set(engine.Load, drive.Rpm / p.LoadRecordedRpm, loaded * (0.55 + (0.45 * drive.Load)) * volume);
    }

    private static void Set(IChannel? channel, double pitch, double volume)
    {
        if (channel is null) return;
        try
        {
            channel.PitchMultiplier = (float)Math.Clamp(pitch, 0.5, 2.5);
            channel.VolumeMultiplier = (float)Math.Clamp(volume, 0.0, 2.0);
        }
        catch
        {
            // A channel the engine has reclaimed is picked up again next frame.
        }
    }

    private IChannel? Keep(IChannel? channel, string id, SpatialAudio spatial, float volume)
    {
        if (channel is not null && SoundChannels.TryMove(channel, spatial)) return channel;
        return Play(id, spatial, volume);
    }

    private void Stop(Vehicle craft, Camera? camera)
    {
        if (!_running.Remove(craft, out Engine? engine)) return;
        Cut(engine);
        if (camera is not null && TrySpatial(camera, craft, out SpatialAudio spatial))
        {
            PlayOnce(engine.Profile.SoundPrefix + "Stop", spatial, 1f);
        }
    }

    private static void Cut(Engine engine)
    {
        if (engine.Idle is not null) SoundChannels.Cut(engine.Idle);
        if (engine.Load is not null) SoundChannels.Cut(engine.Load);
        engine.Idle = engine.Load = null;
    }

    private IChannel? Play(string id, SpatialAudio spatial, float volume)
    {
        try
        {
            if (ModLibrary.Get<SoundBehavior>(id) is not { } sound)
            {
                WarnOnce(id, $"buggy sound '{id}' does not resolve; the engine will be silent");
                return null;
            }
            sound.Play(spatial, volume, out IChannel? channel);
            return channel;
        }
        catch (Exception ex)
        {
            WarnOnce(id, $"buggy sound '{id}' failed: {ex.Message}");
            return null;
        }
    }

    private void PlayOnce(string id, SpatialAudio spatial, float volume) => Play(id, spatial, volume);

    private static bool TrySpatial(Camera camera, Vehicle craft, out SpatialAudio spatial)
    {
        spatial = default!;
        double3 posEgo = camera.GetPositionEgo(craft);
        double3 velEgo = camera.GetVelocityEgo(craft);
        if (!Vec.IsFinite(posEgo) || !Vec.IsFinite(velEgo)) return false;

        spatial = new SpatialAudio(posEgo, velEgo, SoundChannels.PressureAt(camera));
        return true;
    }

    private void WarnOnce(string id, string message)
    {
        if (_warned.Add(id)) Log.Warn(message);
    }
}
