using System.Reflection;
using HarmonyLib;
using KSA;

namespace KSACars;

/// <summary>
/// Where a road is handed to KSA's renderer: a Harmony postfix on
/// <see cref="SuperMeshRenderSystem.ClearBuckets"/>, which KSA calls for each viewport it renders,
/// straight before it draws into it.
///
/// <para>That is the one moment a static mesh's draw survives: one submitted any earlier in the frame
/// is collected and then cleared by this very call before anything is rendered.</para>
///
/// <para>A postfix on a public method, pinned by <see cref="PinTheSignature"/>. If it does not apply
/// no road is drawn.</para>
/// </summary>
internal static class RoadDrawHook
{
    private const string HarmonyId = "com.ksacars.roads";

    private static Harmony? _harmony;
    private static bool _complained;

    public static bool Installed { get; private set; }

    public static void Install()
    {
        if (Installed) return;

        try
        {
            MethodInfo? target = AccessTools.Method(typeof(SuperMeshRenderSystem), nameof(SuperMeshRenderSystem.ClearBuckets),
                [typeof(IViewport)]);
            if (target is null)
            {
                Log.Warn("KSA has no SuperMeshRenderSystem.ClearBuckets to hook; no road is drawn");
                return;
            }

            MethodInfo postfix = typeof(RoadDrawHook).GetMethod(nameof(AfterClearBuckets),
                                                                BindingFlags.NonPublic | BindingFlags.Static)!;
            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            Installed = true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not hook the viewport render; no road is drawn ({e.Message})");
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
            Log.Warn($"could not remove the road hook: {e.Message}");
        }

        _harmony = null;
        Installed = false;
    }

    // Inside the engine's render: nothing here may throw.
    private static void AfterClearBuckets(IViewport viewport)
    {
        if (!Roads.Any) return;

        try
        {
            Roads.Draw(viewport);
        }
        catch (Exception e)
        {
            Roads.Clear();
            if (_complained) return;
            _complained = true;
            Log.Error("drawing a road failed; it is no longer drawn", e);
        }
    }

    // Never called: puts the patched method in this assembly's metadata, and so in docs/KSA-API-SURFACE.md.
    private static void PinTheSignature(SuperMeshRenderSystem system, IViewport viewport) => system.ClearBuckets(viewport);
}
