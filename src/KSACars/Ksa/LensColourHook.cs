using System.Reflection;
using HarmonyLib;
using KSA;

namespace KSACars;

/// <summary>
/// What colour a lamp lens glows: a Harmony postfix on <c>PartTreeRenderData.WriteState</c>.
///
/// <para>KSA's part shader adds a part's emissive texture as white, unless the instance carries the
/// flag its battery status lights use, in which case the texture is a mask and the colour comes with
/// the instance. KSA sets that per whole part; this sets it per subpart, so a tail lens that is its
/// own subpart glows red while the headlamps beside it stay white.</para>
///
/// <para>The method and the batch it writes are private, so there is no signature to pin: if either
/// moves the patch does not apply, the coloured lenses glow white, and the log says so.</para>
/// </summary>
internal static class LensColourHook
{
    private const string HarmonyId = "com.ksacars.lenscolour";

    // The shader's "add emissive colour" state bit.
    private const int ColouredEmissive = 1 << 7;

    private static Harmony? _harmony;
    private static FieldInfo? _flags;
    private static FieldInfo? _colours;
    private static bool _complained;

    public static bool Installed { get; private set; }

    public static void Install()
    {
        if (Installed) return;

        try
        {
            MethodInfo? target = AccessTools.Method(typeof(PartTreeRenderData), "WriteState");
            Type? batch = target?.GetParameters() is [{ } first, ..] ? first.ParameterType : null;
            _flags = batch is null ? null : AccessTools.Field(batch, "StateBitFlags");
            _colours = batch is null ? null : AccessTools.Field(batch, "EmissiveColors");
            if (target is null || _flags?.FieldType != typeof(int[]) || _colours?.FieldType != typeof(uint[]))
            {
                Log.Warn("KSA's part render state has moved; the cars' coloured lenses glow white");
                return;
            }

            MethodInfo postfix = typeof(LensColourHook).GetMethod(nameof(AfterWriteState),
                                                                  BindingFlags.NonPublic | BindingFlags.Static)!;
            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            Installed = true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not hook the part render state; the cars' coloured lenses glow white ({e.Message})");
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
            Log.Warn($"could not remove the lens colour hook: {e.Message}");
        }

        _harmony = null;
        Installed = false;
    }

    // Inside the engine's render-data pass: nothing here may throw.
    private static void AfterWriteState(object inBatch, int inSlot, Part inPart)
    {
        try
        {
            if (Buggies.LensColour(inPart) is not { } rgb) return;
            if (_flags!.GetValue(inBatch) is not int[] flags || _colours!.GetValue(inBatch) is not uint[] colours) return;
            if ((uint)inSlot >= (uint)flags.Length || (uint)inSlot >= (uint)colours.Length) return;

            flags[inSlot] |= ColouredEmissive;
            colours[inSlot] = rgb;
        }
        catch (Exception e)
        {
            if (_complained) return;
            _complained = true;
            Log.Error("colouring a car's lenses failed", e);
        }
    }
}
