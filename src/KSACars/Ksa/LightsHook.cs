using System.Reflection;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace KSACars;

/// <summary>
/// Where a car's headlamps are lit: a Harmony postfix on <see cref="PartTree.UpdateRenderData"/>.
///
/// <para>KSA empties its light list each frame after the GUI pass and fills it again while it builds
/// each craft's render data, which is where its own <c>LightModule</c>s are submitted. A light handed
/// over from any StarMap hook is either cleared before it is drawn or arrives after the frame's lights
/// have been written, so the lamps go in beside KSA's own, once per craft and viewport.</para>
///
/// <para>A postfix on a public method, pinned by <see cref="PinTheSignature"/>. If it does not apply
/// the lamps stay dark and nothing else changes.</para>
/// </summary>
internal static class LightsHook
{
    private const string HarmonyId = "com.ksacars.lights";

    private static Harmony? _harmony;
    private static bool _complained;

    public static bool Installed { get; private set; }

    public static void Install()
    {
        if (Installed) return;

        try
        {
            MethodInfo? target = AccessTools.Method(typeof(PartTree), nameof(PartTree.UpdateRenderData),
                [typeof(double4x4).MakeByRefType(), typeof(bool), typeof(IViewport), typeof(int)]);
            if (target is null)
            {
                Log.Warn("KSA has no PartTree.UpdateRenderData to hook; the cars' headlamps stay dark");
                return;
            }

            MethodInfo postfix = typeof(LightsHook).GetMethod(nameof(AfterRenderData),
                                                              BindingFlags.NonPublic | BindingFlags.Static)!;
            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            Installed = true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not hook the part lights; the cars' headlamps stay dark ({e.Message})");
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
            Log.Warn($"could not remove the lights hook: {e.Message}");
        }

        _harmony = null;
        Installed = false;
    }

    // Inside the engine's render-data pass: nothing here may throw.
    private static void AfterRenderData(PartTree __instance, ref double4x4 matrixAsmb2Ego, IViewport viewport)
    {
        try
        {
            Buggies.LightLamps(__instance, in matrixAsmb2Ego, viewport);
        }
        catch (Exception e)
        {
            if (_complained) return;
            _complained = true;
            Log.Error("lighting a car's headlamps failed", e);
        }
    }

    // Never called: puts the patched method in this assembly's metadata, and so in docs/KSA-API-SURFACE.md.
    private static void PinTheSignature(PartTree parts, IViewport viewport)
    {
        double4x4 matrix = default;
        parts.UpdateRenderData(in matrix, isEditedVehicle: false, viewport, 0);
    }
}
