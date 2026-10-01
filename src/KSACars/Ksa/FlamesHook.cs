using System.Reflection;
using HarmonyLib;
using KSA;

namespace KSACars;

/// <summary>
/// Where a flying car's flames are drawn: a Harmony postfix on
/// <see cref="Vehicle.AddVolumetricExhaustInstances"/>.
///
/// <para>KSA empties its plume list in <c>Program.OnPreRender</c> and fills it again from each craft in
/// frame before it renders, so a plume handed over from any StarMap hook is cleared before it is drawn.
/// The postfix runs once per craft inside that window, beside the craft's own engines.</para>
///
/// <para>A postfix on a public method, pinned by <see cref="PinTheSignature"/>. If it does not apply a
/// car still flies, with nothing coming out of it.</para>
/// </summary>
internal static class FlamesHook
{
    private const string HarmonyId = "com.ksacars.flames";

    private static Harmony? _harmony;
    private static bool _complained;

    public static bool Installed { get; private set; }

    public static void Install()
    {
        if (Installed) return;

        try
        {
            MethodInfo? target = AccessTools.Method(typeof(Vehicle), nameof(Vehicle.AddVolumetricExhaustInstances),
                [typeof(Camera), typeof(VolumetricExhaustRenderer), typeof(double)]);
            if (target is null)
            {
                Log.Warn("KSA has no Vehicle.AddVolumetricExhaustInstances to hook; a flying car shows no flame");
                return;
            }

            MethodInfo postfix = typeof(FlamesHook).GetMethod(nameof(AfterExhausts),
                                                              BindingFlags.NonPublic | BindingFlags.Static)!;
            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            Installed = true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not hook the exhaust plumes; a flying car shows no flame ({e.Message})");
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
            Log.Warn($"could not remove the flames hook: {e.Message}");
        }

        _harmony = null;
        Installed = false;
    }

    // Inside the engine's pre-render pass: nothing here may throw.
    private static void AfterExhausts(Vehicle __instance, Camera camera, VolumetricExhaustRenderer renderer, double frameDeltaTime)
    {
        try
        {
            Buggies.Flames(__instance, camera, renderer, frameDeltaTime);
        }
        catch (Exception e)
        {
            if (_complained) return;
            _complained = true;
            Log.Error("drawing a car's flames failed", e);
        }
    }

    // Never called: puts the patched method in this assembly's metadata, and so in docs/KSA-API-SURFACE.md.
    private static void PinTheSignature(Vehicle vehicle, Camera camera, VolumetricExhaustRenderer renderer) =>
        vehicle.AddVolumetricExhaustInstances(camera, renderer, 0.0);
}
