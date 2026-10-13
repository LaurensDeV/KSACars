using System.Reflection;
using HarmonyLib;
using KSA;

namespace KSACars;

/// <summary>
/// Where the racing display is drawn from: a Harmony postfix on <c>Program.DrawFps</c>.
///
/// <para>With its interface hidden KSA draws none of it, and no StarMap hook is in that frame at all.
/// <c>DrawFps</c> is the one call left between the start of the ImGui frame and its render that is
/// made either way, so what is drawn from it on the foreground list is on the screen with F2 on.</para>
///
/// <para>The method is private, so there is no signature to pin: if it moves the patch does not apply,
/// the log says so, and there is no racing display.</para>
/// </summary>
internal static class RaceHudHook
{
    private const string HarmonyId = "com.ksacars.racehud";

    private static Harmony? _harmony;
    private static bool _complained;

    public static bool Installed { get; private set; }

    public static void Install()
    {
        if (Installed) return;

        try
        {
            MethodInfo? target = AccessTools.Method(typeof(Program), "DrawFps");
            if (target is null || !target.IsStatic || target.GetParameters().Length != 0)
            {
                Log.Warn("KSA's frame counter has moved; there is no racing display with the interface hidden");
                return;
            }

            MethodInfo postfix = typeof(RaceHudHook).GetMethod(nameof(AfterDrawFps), BindingFlags.NonPublic | BindingFlags.Static)!;
            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            Installed = true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not hook the frame counter; there is no racing display with the interface hidden ({e.Message})");
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
            Log.Warn($"could not remove the racing display's hook: {e.Message}");
        }

        _harmony = null;
        Installed = false;
    }

    // Inside KSA's frame: nothing here may throw. Only with the interface hidden, which is when nothing else says any of it.
    private static void AfterDrawFps()
    {
        try
        {
            if (!Program.DrawUI && KsaWorld.InFlightScene) RaceHud.Draw();
        }
        catch (Exception e)
        {
            if (_complained) return;
            _complained = true;
            Log.Error("drawing the racing display failed", e);
        }
    }
}
