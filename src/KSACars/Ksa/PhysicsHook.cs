using System.Reflection;
using HarmonyLib;
using KSA;

namespace KSACars;

/// <summary>
/// The window the cars' springs, tyres and engines are written in: a Harmony prefix on
/// <see cref="Vehicle.PrepareWorker"/>.
///
/// <para>KSA double-buffers a vehicle's state across the frame. <c>ApplyVehicleSolvers</c> writes the
/// worker's result over it, <c>ExecuteNextVehicleSolvers</c> snapshots it for the next worker, and
/// <em>then</em> the GUI pass runs, so an impulse written from any StarMap hook is not in the snapshot
/// and is overwritten before anything reads it. <c>PrepareWorker</c> is the only thing in that window
/// a mod can reach. <c>docs/KSA-FRAME-ORDER.md</c> has the engine code.</para>
///
/// <para>The target is <c>public virtual</c>, and <see cref="PinTheSignature"/> puts it in this
/// assembly's metadata, so <c>tools/api-surface.sh</c> tracks it and a KSA change to it is a build
/// error rather than a car that silently stops driving. Harmony ships with StarMap.</para>
///
/// <para><b>Nothing in the prefix may throw.</b> It runs inside the engine's own frame loop, where an
/// exception is the game rather than a log line.</para>
/// </summary>
internal static class PhysicsHook
{
    private const string HarmonyId = "com.ksacars.physics";

    private static Harmony? _harmony;
    private static bool _complained;

    /// <summary>Whether the patch is in place. False means no car can drive.</summary>
    public static bool Installed { get; private set; }

    public static void Install()
    {
        if (Installed) return;

        try
        {
            MethodInfo? target = typeof(Vehicle).GetMethod(nameof(Vehicle.PrepareWorker), [typeof(SimStep)]);
            if (target is null)
            {
                Log.Warn("cars cannot drive: KSA has no Vehicle.PrepareWorker(SimStep) to hook");
                return;
            }

            MethodInfo prefix = typeof(PhysicsHook).GetMethod(
                nameof(BeforePrepareWorker), BindingFlags.NonPublic | BindingFlags.Static)!;

            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(target, prefix: new HarmonyMethod(prefix));

            Installed = true;
            Log.Info("car physics hooked into Vehicle.PrepareWorker");
        }
        catch (Exception e)
        {
            Log.Error("could not hook car physics; no car can drive", e);
        }
    }

    public static void Remove()
    {
        try
        {
            _harmony?.UnpatchAll(HarmonyId);
        }
        catch (Exception e)
        {
            Log.Warn($"could not remove the physics hook: {e.Message}");
        }

        _harmony = null;
        Installed = false;
    }

    private static void BeforePrepareWorker(Vehicle __instance, SimStep simStep)
    {
        try
        {
            Buggies.Physics(__instance, simStep.DeltaTime);
        }
        catch (Exception e)
        {
            if (_complained) return;
            _complained = true;
            Log.Error("car physics failed inside the engine's step", e);
        }
    }

    // Never called. It exists so the compiler emits a reference to the patched method, which is what
    // puts Vehicle.PrepareWorker in docs/KSA-API-SURFACE.md and turns a signature change in KSA into a
    // build error here.
    private static void PinTheSignature(Vehicle vehicle, SimStep step) => vehicle.PrepareWorker(step);
}
