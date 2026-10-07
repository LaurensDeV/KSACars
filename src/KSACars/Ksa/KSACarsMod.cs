using KSA;
using StarMap.API;

namespace KSACars;

/// <summary>
/// StarMap entry point. StarMap loads the assembly named by mod.toml's EntryAssembly and instantiates
/// the first type carrying <see cref="StarMapModAttribute"/>, then dispatches to the attributed methods.
///
/// <para>The cars' springs, tyres and engines step inside the engine's own physics window, through
/// <see cref="PhysicsHook"/>; what runs here once a frame only finds the cars, poses their wheels and
/// plays their engines. Frame work is wrapped so a fault degrades the mod instead of taking the game
/// down, and repeated faults disable it rather than filling the log.</para>
/// </summary>
[StarMapMod]
public sealed class KSACarsMod
{
    private const int FaultLimit = 10;

    private readonly Buggies _buggies = new();
    private readonly BuggySound _buggySound = new();
    private readonly RocketSound _rocketSound = new();
    private readonly CraftMover _mover = new();
    private readonly RoadEditor _roadEditor = new();
    private readonly FrameLatch _frame = new();
    private Bridge? _bridge;

    private int _faults;
    private bool _disabled;

    [StarMapImmediateLoad]
    public void OnImmediateLoad(Mod mod)
    {
        Log.Info($"loading (mod id: {mod.Id})");
        Log.Info($"KSACars {Build.Version} built for KSA {Build.KsaBuild ?? "?"}, running {Build.KsaRunning ?? "?"}");
    }

    [StarMapAllModsLoaded]
    public void OnFullyLoaded()
    {
        PhysicsHook.Install();

        // KSA draws seated kittens for the craft being flown alone, so in an open car the one left
        // sitting would vanish when the other gets out.
        SeatedCrewHook.Install();
        LightsHook.Install();
        LensColourHook.Install();
        FlamesHook.Install();
        CrashHook.Install();
        BoostHook.Install();
        HudHook.Install();
        RailsHook.Install();
        RoadDrawHook.Install();
        if (Build.Developer) RoadColliders.Install();

        if (Build.Developer) _bridge = new Bridge();
        Log.Info(Build.Developer
                     ? "a developer's install: the bridge is listening"
                     : "a player's install");
    }

    [StarMapAfterOnFrame]
    public void OnAfterFrame(double currentPlayerTime, double dtPlayer)
    {
        try
        {
            if (_disabled) return;

            // The fallback, and a no-op on every frame the GUI pass ran. See StepOnce.
            StepOnce();

            // After the step, so a command sees this frame's world.
            _bridge?.Update(dtPlayer, KsaWorld.InFlightScene && !KsaWorld.IsPaused ? KsaWorld.SimStepSeconds : 0.0);

            // Here and not from the render hook: writing a mesh waits for the graphics card.
            Roads.Update();
        }
        catch (Exception e)
        {
            Fault("frame", e);
        }
        finally
        {
            // Unconditionally: this is the only hook KSA always calls, so a release skipped by an early
            // return or an exception would stop the mod for the rest of the session.
            _frame.EndFrame();
        }
    }

    // The GUI pass is the preferred hook because it runs before the render, so the wheels are drawn
    // where this frame put them; F2 hides the UI and skips that pass, and the frame postfix then
    // poses them a frame late rather than not at all.
    [StarMapAfterGui]
    public void OnAfterGui(double dt)
    {
        try
        {
            if (_disabled) return;
            StepOnce();
            if (!KsaWorld.InFlightScene)
            {
                _mover.Release();
                return;
            }

            CarPanel.Draw(_mover, _roadEditor);
            _roadEditor.Update();
            // After the panel, so a click on its checkbox is the panel's and not the world's; and
            // here rather than in the frame hook, which is past the point gizmos are drawn from.
            _mover.Update();
            _mover.Draw();
        }
        catch (Exception e)
        {
            Fault("gui", e);
        }
    }

    private void StepOnce()
    {
        if (!_frame.Claim()) return;

        if (!KsaWorld.InFlightScene)
        {
            _buggySound.StopAll();
        _rocketSound.StopAll();
            return;
        }

        // Every frame rather than on the clock: the springs step inside the engine's own physics
        // window, so this only has to find the cars and show where their wheels ended up.
        KsaWorld.InvalidateCensus();
        _buggies.Sync(KsaWorld.Vehicles);
        _buggies.Pose();
        _buggySound.Update(_buggies, 1f);
        _rocketSound.Update(_buggies);
    }

    [StarMapUnload]
    public void Unload()
    {
        // Audio channels belong to the game and nothing else gives them back.
        _buggySound.StopAll();
        _rocketSound.StopAll();
        _buggies.Clear();
        PhysicsHook.Remove();
        SeatedCrewHook.Remove();
        LightsHook.Remove();
        LensColourHook.Remove();
        FlamesHook.Remove();
        CrashHook.Remove();
        BoostHook.Remove();
        HudHook.Remove();
        RailsHook.Remove();
        RoadDrawHook.Remove();
            RoadColliders.Remove();
        Log.Info("unloaded");

        // Last: the log batches its writes, so without this the tail of the session never reaches disk.
        Log.Shutdown();
    }

    private void Fault(string where, Exception e)
    {
        _faults++;
        Log.Error($"{where} failed ({_faults}/{FaultLimit})", e);

        if (_faults < FaultLimit) return;

        _disabled = true;
        _buggySound.StopAll();
        _rocketSound.StopAll();
        _buggies.Clear();
        PhysicsHook.Remove();
        SeatedCrewHook.Remove();
        LightsHook.Remove();
        LensColourHook.Remove();
        FlamesHook.Remove();
        CrashHook.Remove();
        BoostHook.Remove();
        HudHook.Remove();
        RailsHook.Remove();
        RoadDrawHook.Remove();
        Log.Error("too many faults - cars disabled for this session");
    }
}
