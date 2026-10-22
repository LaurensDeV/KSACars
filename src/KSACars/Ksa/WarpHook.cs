using System.Reflection;
using HarmonyLib;
using KSA;

namespace KSACars;

/// <summary>
/// The world's speed held by an online race while anyone has joined it: prefixes on both of
/// <see cref="Universe"/>'s <c>SetSimulationSpeed</c>, which every warp key, auto-warp, the escape
/// menu's pause and the editor's go through, replace whatever is asked with the speed
/// <see cref="Multiplayer.HeldSpeed"/> gives. Every copy of the world has to keep the host's time.
///
/// <para>Prefixes on public methods, pinned by <see cref="PinTheSignature"/>. If they do not apply,
/// a racer can warp, and <see cref="Multiplayer"/> puts the speed back the next frame.</para>
/// </summary>
internal static class WarpHook
{
    private const string HarmonyId = "com.ksacars.warp";

    private static Harmony? _harmony;
    private static bool _complained;

    public static bool Installed { get; private set; }

    public static void Install()
    {
        if (Installed) return;

        try
        {
            MethodInfo? plain = AccessTools.Method(typeof(Universe), nameof(Universe.SetSimulationSpeed), [typeof(double), typeof(bool)]);
            MethodInfo? named = AccessTools.Method(typeof(Universe), nameof(Universe.SetSimulationSpeed), [typeof(SimSpeed)]);
            if (plain is null || named is null)
            {
                Log.Warn("KSA has no Universe.SetSimulationSpeed to hook; time warp is put back a frame late in an online race");
                return;
            }

            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(plain, prefix: new HarmonyMethod(typeof(WarpHook).GetMethod(nameof(BeforePlain), BindingFlags.NonPublic | BindingFlags.Static)!));
            _harmony.Patch(named, prefix: new HarmonyMethod(typeof(WarpHook).GetMethod(nameof(BeforeNamed), BindingFlags.NonPublic | BindingFlags.Static)!));
            Installed = true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not hook the simulation speed; time warp is put back a frame late in an online race ({e.Message})");
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
            Log.Warn($"could not remove the warp hook: {e.Message}");
        }

        _harmony = null;
        Installed = false;
    }

    // Inside KSA's input handling and its frame: nothing here may throw.
    private static void BeforePlain(ref double simulationSpeed, ref bool alert)
    {
        if (Held() is not { } held) return;
        if (Math.Abs(held - simulationSpeed) > 1e-9) alert = false;
        simulationSpeed = held;
    }

    private static void BeforeNamed(ref SimSpeed speed)
    {
        if (Held() is { } held && Math.Abs(held - speed.Value) > 1e-9) speed = new SimSpeed(held);
    }

    private static double? Held()
    {
        try
        {
            return Multiplayer.HeldSpeed;
        }
        catch (Exception e)
        {
            if (!_complained)
            {
                _complained = true;
                Log.Error("holding the simulation speed failed", e);
            }
            return null;
        }
    }

    // Never called: puts the patched methods in this assembly's metadata, and so in docs/KSA-API-SURFACE.md.
    private static void PinTheSignature(double speed, bool alert, SimSpeed named)
    {
        Universe.SetSimulationSpeed(speed, alert);
        Universe.SetSimulationSpeed(named);
    }
}
