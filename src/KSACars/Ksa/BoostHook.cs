using System.Reflection;
using Brutal.GlfwApi;
using HarmonyLib;
using KSA;
using RenderCore.Input;

namespace KSACars;

/// <summary>
/// The boost key, held: Harmony postfixes on <see cref="Vehicle.OnKey"/> and
/// <see cref="Vehicle.ProcessInput"/>.
///
/// <para>A vehicle's <c>OnKey</c> turns the keys it knows into actions and queues them; the sprint key
/// is not one of them, so only a kitten on EVA ever hears it. The first postfix queues it for a car
/// that has a boost, the way <c>OnKey</c> queues the engine keys. The second catches it where KSA
/// applies the queue and records it in the craft's own held inputs, so KSA clears it with the rest
/// when the UI takes the keyboard.</para>
///
/// <para>A prefix on <c>OnKey</c> takes Shift off every other key while a car is boosting, because KSA
/// matches a vehicle's keys with their modifiers exactly and would otherwise hear none of them.</para>
///
/// <para>Both are on public methods, pinned by <see cref="PinTheSignature"/>. If either does not
/// apply the boost cannot be lit from the keyboard.</para>
/// </summary>
internal static class BoostHook
{
    private const string HarmonyId = "com.ksacars.boost";

    private static Harmony? _harmony;
    private static bool _complained;

    public static bool Installed { get; private set; }

    public static void Install()
    {
        if (Installed) return;

        try
        {
            MethodInfo? target = AccessTools.Method(typeof(Vehicle), nameof(Vehicle.ProcessInput),
                [typeof(InputAction), typeof(GlfwKeyAction), typeof(GlfwModifier)]);
            if (target is null)
            {
                Log.Warn("KSA has no Vehicle.ProcessInput to hook; a car's boost cannot be lit from the keyboard");
                return;
            }

            MethodInfo? onKey = AccessTools.Method(typeof(Vehicle), nameof(Vehicle.OnKey), [typeof(GlfwKeyEvent)]);
            if (onKey is null)
            {
                Log.Warn("KSA has no Vehicle.OnKey to hook; a car's boost cannot be lit from the keyboard");
                return;
            }

            MethodInfo postfix = typeof(BoostHook).GetMethod(nameof(AfterInput), BindingFlags.NonPublic | BindingFlags.Static)!;
            MethodInfo keyed = typeof(BoostHook).GetMethod(nameof(AfterKey), BindingFlags.NonPublic | BindingFlags.Static)!;
            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            MethodInfo unshifted = typeof(BoostHook).GetMethod(nameof(BeforeKey), BindingFlags.NonPublic | BindingFlags.Static)!;
            _harmony.Patch(onKey, prefix: new HarmonyMethod(unshifted), postfix: new HarmonyMethod(keyed));
            Installed = true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not hook vehicle input; a car's boost cannot be lit from the keyboard ({e.Message})");
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
            Log.Warn($"could not remove the boost hook: {e.Message}");
        }

        _harmony = null;
        Installed = false;
    }

    // KSA matches the steering, throttle and engine keys with their modifiers exactly, so with Shift
    // held as the boost none of them is heard, and one released under it is never let go. While a car
    // is boosting, every other key reaches it as if Shift were up. Nothing here may throw.
    private static void BeforeKey(Vehicle __instance, ref GlfwKeyEvent keyEvent)
    {
        try
        {
            if (keyEvent.IsMouse || (keyEvent.Mods & GlfwModifier.Shift) == 0) return;
            if (!__instance.GetSprintInput() || Input.Contains(in keyEvent, InputAction.Sprint)) return;
            if (Buggies.Of(__instance) is not { } car || !car.Drive.Profile.HasBoost) return;

            keyEvent = new GlfwKeyEvent(keyEvent.Window, keyEvent.Action, keyEvent.Key, keyEvent.Mods & ~GlfwModifier.Shift);
        }
        catch (Exception e)
        {
            if (_complained) return;
            _complained = true;
            Log.Error("reading a key under the boost failed", e);
        }
    }

    // Inside the engine's key handling: nothing here may throw.
    private static void AfterKey(Vehicle __instance, GlfwKeyEvent keyEvent)
    {
        try
        {
            if (keyEvent.Action == GlfwKeyAction.Repeat || !Input.Contains(in keyEvent, InputAction.Sprint)) return;
            if (Buggies.Of(__instance) is not { } car || !car.Drive.Profile.HasBoost) return;

            InputEvents.VehicleInputBuffer.Add(new InputEvents.VehicleInputData
            {
                Vehicle = __instance,
                Action = InputAction.Sprint,
                KeyAction = keyEvent.Action,
                Modifiers = keyEvent.Mods,
            });
        }
        catch (Exception e)
        {
            if (_complained) return;
            _complained = true;
            Log.Error("reading the boost key failed", e);
        }
    }

    // Inside the engine's input dispatch: nothing here may throw.
    private static void AfterInput(Vehicle __instance, InputAction action, GlfwKeyAction keyAction)
    {
        try
        {
            if (keyAction == GlfwKeyAction.Repeat || Buggies.Of(__instance) is not { } car) return;

            // The RCS key, which a car has nothing else to do with, switches the downforce rockets.
            if (action == InputAction.ToggleRCS && keyAction == GlfwKeyAction.Press && car.Drive.Profile.HasDownforce)
            {
                car.Downforce = !car.Downforce;
            }

            if (action != InputAction.Sprint || !car.Drive.Profile.HasBoost) return;

            KsaWorld.TrySetSprint(__instance, keyAction == GlfwKeyAction.Press);
        }
        catch (Exception e)
        {
            if (_complained) return;
            _complained = true;
            Log.Error("reading the boost key failed", e);
        }
    }

    // Never called: puts the patched method in this assembly's metadata, and so in docs/KSA-API-SURFACE.md.
    private static void PinTheSignature(Vehicle vehicle)
    {
        vehicle.ProcessInput(InputAction.Sprint, GlfwKeyAction.Press, default);
        vehicle.OnKey(default);
    }
}
