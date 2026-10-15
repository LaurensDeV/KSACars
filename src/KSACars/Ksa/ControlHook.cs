using System.Reflection;
using HarmonyLib;
using KSA;

namespace KSACars;

/// <summary>
/// Nobody takes another racer's car in an online race: a prefix on the setter of
/// <see cref="Program.ControlledVehicle"/>, which clicking a craft, the map view, EVA, boarding and
/// the editor all go through, leaves control where it is when the craft asked for is
/// <see cref="Multiplayer.IsOthers"/>; and one on <see cref="KittenEva.BoardVehicle"/> turns a kitten
/// away from such a car before it climbs in, since by the time control is handed over it has gone.
///
/// <para>Prefixes on public members, pinned by <see cref="PinTheSignature"/>. If they do not apply,
/// <see cref="Multiplayer"/> hands this game's own car back the next frame.</para>
/// </summary>
internal static class ControlHook
{
    private const string HarmonyId = "com.ksacars.control";

    private static Harmony? _harmony;
    private static bool _complained;

    public static bool Installed { get; private set; }

    public static void Install()
    {
        if (Installed) return;

        try
        {
            MethodInfo? setter = AccessTools.PropertySetter(typeof(Program), nameof(Program.ControlledVehicle));
            MethodInfo? board = AccessTools.Method(typeof(KittenEva), nameof(KittenEva.BoardVehicle), [typeof(Vehicle), typeof(EVADoor)]);
            if (setter is null || board is null)
            {
                Log.Warn("KSA has no Program.ControlledVehicle or KittenEva.BoardVehicle to hook; another racer's car is handed back a frame late");
                return;
            }

            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(setter, prefix: new HarmonyMethod(typeof(ControlHook).GetMethod(nameof(BeforeControl), BindingFlags.NonPublic | BindingFlags.Static)!));
            _harmony.Patch(board, prefix: new HarmonyMethod(typeof(ControlHook).GetMethod(nameof(BeforeBoard), BindingFlags.NonPublic | BindingFlags.Static)!));
            Installed = true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not hook taking control; another racer's car is handed back a frame late ({e.Message})");
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
            Log.Warn($"could not remove the control hook: {e.Message}");
        }

        _harmony = null;
        Installed = false;
    }

    // False leaves control where it is. Inside KSA's own input and frame: nothing here may throw.
    private static bool BeforeControl(Vehicle? value) => !Others(value);

    private static bool BeforeBoard(Vehicle host, ref bool __result)
    {
        if (!Others(host)) return true;
        __result = false;
        return false;
    }

    private static bool Others(Vehicle? craft)
    {
        try
        {
            return Multiplayer.IsOthers(craft);
        }
        catch (Exception e)
        {
            if (!_complained)
            {
                _complained = true;
                Log.Error("telling another racer's car from this game's failed", e);
            }
            return false;
        }
    }

    // Never called: puts the patched members in this assembly's metadata, and so in docs/KSA-API-SURFACE.md.
    private static void PinTheSignature(Vehicle craft, KittenEva kitten, EVADoor door)
    {
        Program.ControlledVehicle = craft;
        kitten.BoardVehicle(craft, door);
    }
}
