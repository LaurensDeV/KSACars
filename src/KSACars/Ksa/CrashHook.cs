using System.Reflection;
using HarmonyLib;
using KSA;

namespace KSACars;

/// <summary>
/// A car that comes down hard is not blown up: a Harmony prefix on
/// <see cref="Universe.DestroyVehicleFromEvent"/>, the one call every structural failure ends in.
///
/// <para>KSA breaks a craft up past a g-load set by its size, which a car dropped a few metres onto
/// its bump stops exceeds. The prefix turns away a car's ground impacts, collisions and g-loads, and
/// leaves the sea and the air to do what they do to anything else.</para>
///
/// <para>A prefix on a public method, pinned by <see cref="PinTheSignature"/>. If it does not apply,
/// a car breaks up as any craft does.</para>
/// </summary>
internal static class CrashHook
{
    private const string HarmonyId = "com.ksacars.crash";

    private static Harmony? _harmony;
    private static bool _complained;

    public static bool Installed { get; private set; }

    public static void Install()
    {
        if (Installed) return;

        try
        {
            MethodInfo? target = AccessTools.Method(typeof(Universe), nameof(Universe.DestroyVehicleFromEvent),
                                                    [typeof(Vehicle), typeof(VehicleDestructionEvent)]);
            if (target is null)
            {
                Log.Warn("KSA has no Universe.DestroyVehicleFromEvent to hook; a car breaks up in a hard landing");
                return;
            }

            MethodInfo prefix = typeof(CrashHook).GetMethod(nameof(BeforeDestroy),
                                                            BindingFlags.NonPublic | BindingFlags.Static)!;
            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            Installed = true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not hook vehicle destruction; a car breaks up in a hard landing ({e.Message})");
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
            Log.Warn($"could not remove the crash hook: {e.Message}");
        }

        _harmony = null;
        Installed = false;
    }

    // False spares the craft. Inside the engine's own loop: nothing here may throw.
    private static bool BeforeDestroy(Vehicle vehicle, VehicleDestructionEvent destructionEvent)
    {
        try
        {
            if (Buggies.Of(vehicle) is null) return true;

            return destructionEvent.Cause is not (VehicleDestructionCause.GroundImpact
                or VehicleDestructionCause.Collision or VehicleDestructionCause.ExcessiveGForce);
        }
        catch (Exception e)
        {
            if (!_complained)
            {
                _complained = true;
                Log.Error("sparing a car from a crash failed", e);
            }
            return true;
        }
    }

    // Never called: puts the patched method in this assembly's metadata, and so in docs/KSA-API-SURFACE.md.
    private static void PinTheSignature(Vehicle vehicle, VehicleDestructionEvent destructionEvent) =>
        Universe.DestroyVehicleFromEvent(vehicle, destructionEvent);
}
