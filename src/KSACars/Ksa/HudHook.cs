using System.Reflection;
using HarmonyLib;
using KSA;

namespace KSACars;

/// <summary>
/// KSA's flight HUD told that a car with rockets has engines: Harmony postfixes on
/// <see cref="GaugeCanvas.IsContextVisible"/> and <see cref="Vehicle.AreAllEnginesInactive"/>.
///
/// <para>KSA shows its engine panel, which is where the throttle is read, only on a craft carrying an
/// <c>EngineController</c>, and raises "No active engines" whenever the engine switch is on and no
/// engine is running. A car's rockets are the mod's, so it has neither. The first postfix shows the
/// panel for a car that flies; the second withholds the alert from it.</para>
///
/// <para>Both are on public methods, pinned by <see cref="PinTheSignature"/>. If either does not
/// apply the car flies as before, without the panel or with the alert.</para>
/// </summary>
internal static class HudHook
{
    private const string HarmonyId = "com.ksacars.hud";

    private static Harmony? _harmony;
    private static bool _complained;

    public static bool Installed { get; private set; }

    public static void Install()
    {
        if (Installed) return;

        try
        {
            MethodInfo? visible = AccessTools.Method(typeof(GaugeCanvas), nameof(GaugeCanvas.IsContextVisible), Type.EmptyTypes);
            MethodInfo? inactive = AccessTools.Method(typeof(Vehicle), nameof(Vehicle.AreAllEnginesInactive), Type.EmptyTypes);
            if (visible is null || inactive is null)
            {
                Log.Warn("KSA's engine panel or its alert has moved; a flying car shows no throttle");
                return;
            }

            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(visible, postfix: new HarmonyMethod(
                typeof(HudHook).GetMethod(nameof(AfterContextVisible), BindingFlags.NonPublic | BindingFlags.Static)!));
            _harmony.Patch(inactive, postfix: new HarmonyMethod(
                typeof(HudHook).GetMethod(nameof(AfterEnginesInactive), BindingFlags.NonPublic | BindingFlags.Static)!));
            Installed = true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not hook the engine panel; a flying car shows no throttle ({e.Message})");
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
            Log.Warn($"could not remove the HUD hook: {e.Message}");
        }

        _harmony = null;
        Installed = false;
    }

    private static bool FlyingCarIsFlown =>
        Program.ControlledVehicle is { } craft && Buggies.Of(craft) is { } car && car.Drive.Profile.HasRockets;

    // Inside the engine's HUD pass: nothing here may throw.
    private static void AfterContextVisible(GaugeCanvas __instance, ref bool __result)
    {
        try
        {
            // Only a canvas whose one condition is engines: any other it names is KSA's to judge.
            if (__result || __instance.VisibleInContext is not [GaugeVisibilityFlag.Engines]) return;
            if (FlyingCarIsFlown) __result = true;
        }
        catch (Exception e)
        {
            Complain(e);
        }
    }

    private static void AfterEnginesInactive(ref bool __result)
    {
        try
        {
            if (__result && FlyingCarIsFlown) __result = false;
        }
        catch (Exception e)
        {
            Complain(e);
        }
    }

    private static void Complain(Exception e)
    {
        if (_complained) return;
        _complained = true;
        Log.Error("showing a car's rockets on the HUD failed", e);
    }

    // Never called: puts the patched methods in this assembly's metadata, and so in docs/KSA-API-SURFACE.md.
    private static bool PinTheSignature(GaugeCanvas canvas) => canvas.IsContextVisible() || Vehicle.AreAllEnginesInactive();
}
