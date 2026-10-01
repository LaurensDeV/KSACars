using System.Reflection;
using HarmonyLib;
using KSA;

namespace KSACars;

/// <summary>
/// The crew of an open car, drawn when it is not the craft being flown.
///
/// <para>KSA draws seated kittens for <c>Program.ControlledVehicle</c> alone, which suits a capsule,
/// whose crew is inside it. A buggy's is out in the open: when one kitten gets out and control
/// goes to it, the one still sitting in the car would not be drawn. The postfix runs once per viewport, where
/// the controlled craft's crew has just been drawn, and draws every other buggy's the same way.</para>
///
/// <para>A postfix on a public method, so <see cref="PinTheSignature"/> puts it in the API record and
/// a change to it is a build error. If it does not apply, the crew of a car nobody is flying is not
/// drawn, which is what KSA does anyway.</para>
/// </summary>
internal static class SeatedCrewHook
{
    private const string HarmonyId = "com.ksacars.seatedcrew";

    private static Harmony? _harmony;
    private static bool _complained;

    public static bool Installed { get; private set; }

    public static void Install()
    {
        if (Installed) return;

        try
        {
            MethodInfo? target = AccessTools.Method(typeof(Vehicle), nameof(Vehicle.UpdateSeatedCrewRenderData),
                                                    [typeof(IViewport), typeof(int)]);
            if (target is null)
            {
                Log.Warn("KSA has no Vehicle.UpdateSeatedCrewRenderData to hook; a buggy's crew is drawn only while it is flown");
                return;
            }

            MethodInfo postfix = typeof(SeatedCrewHook).GetMethod(nameof(AfterSeatedCrew),
                                                                  BindingFlags.NonPublic | BindingFlags.Static)!;
            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            Installed = true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not hook seated crew drawing; a buggy's crew is drawn only while it is flown ({e.Message})");
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
            Log.Warn($"could not remove the seated crew hook: {e.Message}");
        }

        _harmony = null;
        Installed = false;
    }

    // Inside the engine's render pass: nothing here may throw.
    private static void AfterSeatedCrew(IViewport viewport, int inFrameIndex)
    {
        try
        {
            if (viewport.Is(ViewportType.CharacterPortrait)) return;
            Buggies.DrawCrewOfUnflown(viewport, inFrameIndex);
        }
        catch (Exception e)
        {
            if (_complained) return;
            _complained = true;
            Log.Error("drawing a buggy's crew failed", e);
        }
    }

    // Never called: puts the patched method in this assembly's metadata, and so in docs/KSA-API-SURFACE.md.
    private static void PinTheSignature(Vehicle vehicle, IViewport viewport) => vehicle.UpdateSeatedCrewRenderData(viewport, 0);
}
