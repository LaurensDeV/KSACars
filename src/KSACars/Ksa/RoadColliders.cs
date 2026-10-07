using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using HarmonyLib;
using KSA;
using double3 = Brutal.Numerics.double3;
using double4x4 = Brutal.Numerics.double4x4;
using doubleQuat = Brutal.Numerics.doubleQuat;

namespace KSACars;

/// <summary>
/// Boxes of the mod's own in KSA's physics, so a hull, a kitten and another craft meet a road as they
/// meet the ground. **An experiment**: one box from the bridge, to find out whether anything rests on it.
///
/// <para>A static only collides if the engine's narrow phase lets it: its own terrain and launch pad,
/// terrain blocks, and ground clutter. So each box is registered as clutter with infinite mass, which is
/// also what makes a kitten's locomotion count it as ground and what stops a hit knocking it loose. The
/// registration is repeated every pass, because clutter's own bookkeeping clears it.</para>
///
/// <para>One physics simulation belongs to each bubble and steps on a worker thread, so the boxes are
/// synced from prefixes on its collision passes, against a snapshot swapped in from the main thread.
/// Shapes live in a registry every simulation shares and that is only writable between vehicle steps, so
/// they are made there, once a size. When a simulation is recycled the engine clears its statics, and its
/// state here is dropped. Carried over from KSAGolf, where it ran without error and was never seen to
/// hold anything up.</para>
/// </summary>
internal static class RoadColliders
{
    private const string HarmonyId = "com.ksacars.roadcolliders";

    // Past this a box is too far from a bubble to matter to anything in it.
    private const double ReachM = 3000.0;

    private sealed record Box(double3 CentreCcf, Quaternion Orientation, TypedIndex Shape);

    private sealed record Wanted(Celestial Body, (double3 Centre, doubleQuat Orientation, double3 Size)[] Boxes, int Version);

    private sealed record Ready(Celestial Body, Box[] Boxes, int Version);

    private sealed class SimState
    {
        public StaticHandle[] Handles = [];
        public int Version = -1;
        public double3 Bub;
    }

    private static readonly ConditionalWeakTable<ConstraintSim, SimState> States = new();
    private static readonly Dictionary<(int, int, int), TypedIndex> ShapesBySize = [];

    private static Harmony? _harmony;
    private static volatile Wanted? _wanted;
    private static volatile Ready? _ready;
    private static int _version;
    private static bool _complained;

    private static FieldInfo? _clutterStatics;
    private static object? _solidClutter;

    public static bool Installed { get; private set; }

    /// <summary>How many times boxes have been put into a simulation, and how many simulations hold them now.</summary>
    public static int Adds;

    public static void Install()
    {
        if (Installed) return;

        try
        {
            _clutterStatics = AccessTools.Field(typeof(BubbleClutterStatics), "_statics");
            Type? info = _clutterStatics?.FieldType.GetGenericArguments() is [_, var value] ? value : null;
            ConstructorInfo? make = info?.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(c => c.GetParameters().Length == 2);
            if (_clutterStatics is null || make is null)
            {
                Log.Warn("roads have no colliders: KSA's ground clutter no longer has the _statics a box registers in");
                return;
            }

            ParameterInfo[] args = make.GetParameters();
            _solidClutter = make.Invoke([Activator.CreateInstance(args[0].ParameterType), float.PositiveInfinity]);

            _harmony = new Harmony(HarmonyId);
            Patch(typeof(Universe), nameof(Universe.ExecuteNextVehicleSolvers), nameof(BetweenVehicleSteps));
            Patch(typeof(ConstraintSim), nameof(ConstraintSim.DetectCollisions), nameof(BeforeCollisions));
            Patch(typeof(ConstraintSim), nameof(ConstraintSim.Simulate), nameof(BeforeCollisions));
            Patch(typeof(ConstraintSim), nameof(ConstraintSim.TryResetForPool), nameof(BeforeRecycle));
            Installed = true;
            Log.Info("road colliders hooked: boxes go into each physics bubble as clutter statics");
        }
        catch (Exception e)
        {
            Log.Warn($"roads have no colliders: could not hook the physics ({e.GetBaseException().Message})");
            try { _harmony?.UnpatchAll(HarmonyId); } catch { /* Unhooking a half-made patch. */ }
            _harmony = null;
        }
    }

    private static void Patch(Type type, string method, string prefix)
    {
        MethodInfo target = AccessTools.Method(type, method) ?? throw new MissingMethodException(type.Name, method);
        _harmony!.Patch(target, prefix: new HarmonyMethod(typeof(RoadColliders).GetMethod(prefix, BindingFlags.NonPublic | BindingFlags.Static)));
    }

    public static void Remove()
    {
        try { _harmony?.UnpatchAll(HarmonyId); } catch { /* Unloading anyway. */ }
        _harmony = null;
        Installed = false;
        _wanted = null;
        _ready = null;
    }

    /// <summary>The boxes wanted on a body, from the main thread; null takes them away.</summary>
    public static void Want(Celestial? body, IEnumerable<(double3 Centre, doubleQuat Orientation, double3 Size)> boxes)
    {
        _version++;
        (double3, doubleQuat, double3)[] list = [.. boxes];
        _wanted = body is null || list.Length == 0 ? null : new Wanted(body, list, _version);
        if (_wanted is null) _ready = new Ready(null!, [], _version);
    }

    /// <summary>A box as the physics wants it: its centre, and its axes as a body-fixed rotation.</summary>
    public static (double3 Centre, doubleQuat Orientation, double3 Size) Place(double3 centreCcf, double3 xCcf, double3 yCcf, double3 zCcf, double3 size)
        => (centreCcf, doubleQuat.CreateFromRotationMatrix(new double4x4(xCcf.X, xCcf.Y, xCcf.Z, 0.0, yCcf.X, yCcf.Y, yCcf.Z, 0.0,
                                                                         zCcf.X, zCcf.Y, zCcf.Z, 0.0, 0.0, 0.0, 0.0, 1.0)), size);

    // Main thread, between vehicle steps: the one moment the shared shape registry can be written.
    private static void BetweenVehicleSteps()
    {
        if (_wanted is not { } wanted || _ready?.Version == wanted.Version) return;

        try
        {
            using ShapesUnlock unlocked = ConstraintSim.UnlockShapes();
            Box[] boxes = [.. wanted.Boxes.Select(b => new Box(b.Centre, ToBepu(b.Orientation), ShapeOf(unlocked.Shapes, b.Size)))];
            _ready = new Ready(wanted.Body, boxes, wanted.Version);
            Log.Info($"road colliders: {boxes.Length} box(es), {ShapesBySize.Count} shape(s) made so far");
        }
        catch (Exception e)
        {
            Complain($"could not make the boxes' shapes yet, will try again: {e.GetBaseException().Message}");
        }
    }

    // Shapes are shared by every simulation and never freed.
    private static TypedIndex ShapeOf(Shapes shapes, double3 size)
    {
        var key = ((int)Math.Round(size.X * 1e4), (int)Math.Round(size.Y * 1e4), (int)Math.Round(size.Z * 1e4));
        if (!ShapesBySize.TryGetValue(key, out TypedIndex index))
        {
            index = shapes.Add(new BepuPhysics.Collidables.Box((float)size.X, (float)size.Y, (float)size.Z));
            ShapesBySize[key] = index;
        }

        return index;
    }

    // A worker thread, before a bubble's collision pass. Nothing here may throw.
    private static void BeforeCollisions(ConstraintSim __instance)
    {
        try
        {
            Sync(__instance);
        }
        catch (Exception e)
        {
            Complain($"the road colliders could not be synced: {e.GetBaseException().Message}");
        }
    }

    private static void Sync(ConstraintSim sim)
    {
        Ready? ready = _ready;
        SimState state = States.GetOrCreateValue(sim);

        if (!TryOrigin(sim, out bool ccf, out IParentBody? parent, out double3 bub)) return;

        bool here = ready is { Boxes.Length: > 0 } && ccf && ReferenceEquals(parent, ready.Body)
                    && Vector3Distance(ready.Boxes[0].CentreCcf, bub) < ReachM;
        if (!here)
        {
            if (state.Handles.Length > 0) Clear(sim, state);
            return;
        }

        if (state.Version != ready!.Version)
        {
            Clear(sim, state);
            state.Handles = [.. ready.Boxes.Select(b => sim.Simulation.Statics.Add(Describe(b, bub), ref KSA.StaticsShouldntAwakenBodies.Shared))];
            state.Version = ready.Version;
            state.Bub = bub;
            Interlocked.Increment(ref Adds);
        }
        else if (state.Bub != bub)
        {
            for (int k = 0; k < state.Handles.Length; k++) sim.Simulation.Statics.ApplyDescription(state.Handles[k], Describe(ready.Boxes[k], bub), ref KSA.StaticsShouldntAwakenBodies.Shared);
            state.Bub = bub;
        }

        if (sim.ClutterStatics is { } clutter && _clutterStatics?.GetValue(clutter) is IDictionary registered)
        {
            foreach (StaticHandle handle in state.Handles) registered[handle] = _solidClutter;
        }
    }

    private static double Vector3Distance(double3 a, double3 b) => (a - b).Length();

    private static StaticDescription Describe(Box box, double3 bub)
    {
        double3 at = box.CentreCcf - bub;
        return new StaticDescription { Pose = new RigidPose(new Vector3((float)at.X, (float)at.Y, (float)at.Z), box.Orientation), Shape = box.Shape };
    }

    private static void Clear(ConstraintSim sim, SimState state)
    {
        IDictionary? registered = sim.ClutterStatics is { } clutter ? _clutterStatics?.GetValue(clutter) as IDictionary : null;
        foreach (StaticHandle handle in state.Handles)
        {
            registered?.Remove(handle);
            sim.Simulation.Statics.Remove(handle);
        }

        state.Handles = [];
        state.Version = -1;
    }

    private static bool TryOrigin(ConstraintSim sim, out bool ccf, out IParentBody? parent, out double3 bub)
    {
        foreach (VehicleUpdateState vehicle in sim.HandleToState.Values)
        {
            ReadOnlyPhysicsStates states = vehicle.GetReadOnlyStates();
            ccf = states.Origin.BubFrame.IsCcf();
            parent = states.Origin.Parent;
            bub = states.Origin.PositionBub;
            return true;
        }

        (ccf, parent, bub) = (false, null, default);
        return false;
    }

    // The engine clears a recycled simulation's statics itself; what was held for it here goes with them.
    private static void BeforeRecycle(ConstraintSim __instance) => States.Remove(__instance);

    private static Quaternion ToBepu(doubleQuat q) => new((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);

    private static void Complain(string what)
    {
        if (_complained) return;
        _complained = true;
        Log.Warn(what);
    }

    // Never called. Puts the patched methods in this assembly's metadata for the API record.
    private static void PinThePatches(ConstraintSim sim, double dt, SimStep step)
    {
        sim.DetectCollisions(dt);
        sim.Simulate(dt, in step);
        sim.TryResetForPool();
    }
}
