using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;
using KSA;

namespace KSACars;

/// <summary>
/// A car whose rockets are burning is kept under full physics above the atmosphere: a Harmony postfix on
/// <c>PhysicsBubble.KittenWantsWake</c>.
///
/// <para>KSA puts a craft that is out of the air onto its orbit unless one of its engines is firing,
/// and a car's rockets are not engines. On its orbit nothing the mod writes to its velocity is read, so
/// the car coasts and falls until the air gives it back. KSA asks a handful of things before it rails a
/// craft, in both places it decides; this is the one that is asked per craft and is a method of its own,
/// there for a kitten holding on to something, and a burning car answers yes to it.</para>
///
/// <para>The method is private, so there is no signature to pin: if it moves the patch does not apply,
/// the log says so, and a car's rockets stop pushing above the atmosphere.</para>
/// </summary>
internal static class RailsHook
{
    private const string HarmonyId = "com.ksacars.rails";

    // Written in the physics window and read from KSA's physics worker.
    private static readonly ConcurrentDictionary<Vehicle, byte> Burning = new();

    private static Harmony? _harmony;
    private static bool _complained;

    public static bool Installed { get; private set; }

    /// <summary>Says whether a car's rockets are pushing, which is what keeps it off its orbit.</summary>
    public static void Mark(Vehicle craft, bool burning)
    {
        if (burning) Burning[craft] = 0;
        else Burning.TryRemove(craft, out _);
    }

    public static void Forget(Vehicle craft) => Burning.TryRemove(craft, out _);

    public static void Install()
    {
        if (Installed) return;

        try
        {
            MethodInfo? target = AccessTools.Method(typeof(PhysicsBubble), "KittenWantsWake");
            ParameterInfo[]? parameters = target?.GetParameters();
            if (target is null || target.ReturnType != typeof(bool)
                || parameters is not [{ } first, ..] || first.ParameterType != typeof(VehicleUpdateState))
            {
                Log.Warn("KSA's wake check has moved; a car's rockets stop pushing above the atmosphere");
                return;
            }

            MethodInfo postfix = typeof(RailsHook).GetMethod(nameof(AfterWantsWake),
                                                             BindingFlags.NonPublic | BindingFlags.Static)!;
            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            Installed = true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not hook the wake check; a car's rockets stop pushing above the atmosphere ({e.Message})");
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
            Log.Warn($"could not remove the rails hook: {e.Message}");
        }

        _harmony = null;
        Installed = false;
        Burning.Clear();
    }

    // Inside KSA's physics worker: nothing here may throw. The first parameter is matched by position,
    // whatever KSA names it.
    private static void AfterWantsWake(VehicleUpdateState __0, ref bool __result)
    {
        try
        {
            if (!__result && __0?.ReadOnlyVehicle is { } craft && Burning.ContainsKey(craft)) __result = true;
        }
        catch (Exception e)
        {
            if (_complained) return;
            _complained = true;
            Log.Error("keeping a burning car off its orbit failed", e);
        }
    }
}
