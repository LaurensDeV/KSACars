using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuUtilities.Memory;
using HarmonyLib;
using KSA;
using double3 = Brutal.Numerics.double3;
using double4x4 = Brutal.Numerics.double4x4;
using doubleQuat = Brutal.Numerics.doubleQuat;

namespace KSACars;

/// <summary>
/// Solids of the mod's own in KSA's physics, so a hull, a kitten and another craft meet a road as they
/// meet the ground: one triangle mesh for each piece of road that is joined, runs and the junctions
/// they stop at, of the triangles it is drawn with, and
/// the experiment's box.
///
/// <para>A static only collides if the engine's narrow phase lets it: its own terrain and launch pad,
/// terrain blocks, and ground clutter. So each is registered as clutter with infinite mass, which is
/// also what makes a kitten's locomotion count it as ground and what stops a hit knocking it loose. The
/// registration is looked for every pass and made again, because clutter's own bookkeeping clears it.</para>
///
/// <para>One physics simulation belongs to each bubble and steps on a worker thread, so the statics are
/// synced from prefixes on its collision passes, against a laying swapped in whole from the main thread.
/// A bubble holds the solids any of which is within <see cref="ReachM"/> of its origin and no others.</para>
///
/// <para>Shapes live in a registry every simulation shares and that is only writable between vehicle
/// steps, when no simulation is stepping. A mesh's triangles and its tree are built when the road is
/// laid, in a pool of the mod's own, and only entered in the registry there. A laying that has been
/// replaced is taken out of it there too, and its memory returned, once no simulation still has a
/// static of it: each simulation counts itself onto the laying it holds and off it again.</para>
/// </summary>
internal static class RoadColliders
{
    private const string HarmonyId = "com.ksacars.roadcolliders";

    // Past this from a bubble's origin, nothing in the bubble can meet a solid.
    private const double ReachM = 3000.0;

    // A bubble that has a solid keeps it this much further out, so one at the edge is not given and
    // taken it every pass.
    private const double KeepM = 300.0;

    private sealed record Solid(double3 AtCcf, double RadiusM, Quaternion Orientation, TypedIndex Shape);

    // Built on the main thread and not yet in the registry.
    private sealed record Wanted(Celestial? Body, (double3 AtCcf, double RadiusM, Mesh Mesh)[] Meshes,
                                 (double3 Centre, doubleQuat Orientation, double3 Size)[] Boxes);

    private sealed class Laying(Celestial? body, Solid[] solids, TypedIndex[] meshes)
    {
        public readonly Celestial? Body = body;
        public readonly Solid[] Solids = solids;
        public readonly TypedIndex[] Meshes = meshes;

        // How many simulations have statics of this laying's shapes.
        public int Holders;
    }

    private sealed class SimState
    {
        public Laying? Laying;

        // One a solid of the laying: the static it is in this simulation, and that handle boxed once,
        // as the engine's own dictionary of clutter is keyed through reflection. Null while it is out of reach.
        public StaticHandle[] Handles = [];
        public object?[] Keys = [];
        public double3 Bub;

        // A simulation dropped without being recycled takes its statics with it.
        ~SimState() => Let(this);
    }

    private static readonly ConditionalWeakTable<ConstraintSim, SimState> States = new();
    private static readonly Dictionary<(int, int, int), TypedIndex> ShapesBySize = [];

    // The meshes' triangles and trees. The registry's own pool is KSA's, and is only safe in the window.
    private static readonly BufferPool Pool = new();
    private static readonly object Gate = new();
    private static readonly List<Laying> Replaced = [];

    private static Harmony? _harmony;
    private static Wanted? _wanted;
    private static volatile Laying? _laying;
    private static bool _complained;

    private static FieldInfo? _clutterStatics;
    private static object? _solidClutter;

    public static bool Installed { get; private set; }

    /// <summary>How many statics have been put into simulations.</summary>
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
                Log.Warn("roads have no colliders: KSA's ground clutter no longer has the _statics a solid registers in");
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
            Log.Info("road colliders hooked: meshes go into each physics bubble as clutter statics");
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

    // What is in the registry stays there, and in the pool: a simulation may hold a static of it, and
    // with the prefixes gone nothing takes one out.
    public static void Remove()
    {
        try { _harmony?.UnpatchAll(HarmonyId); } catch { /* Unloading anyway. */ }
        _harmony = null;
        Installed = false;
        lock (Gate) Forget(ref _wanted);
        _laying = null;
    }

    /// <summary>
    /// The solids wanted on a body, from the main thread: every joined piece of road and the experiment's
    /// boxes. Nothing of either takes them all away. The meshes are built here.
    /// </summary>
    public static void Want(Celestial? body, IReadOnlyList<RoadCollider> roads, IReadOnlyList<(double3 Centre, doubleQuat Orientation, double3 Size)> boxes)
    {
        if (!Installed) return;

        lock (Gate)
        {
            Forget(ref _wanted);
            List<(double3, double, Mesh)> meshes = [];
            try
            {
                foreach (RoadCollider road in body is null ? [] : roads) meshes.Add((road.Origin, road.RadiusM, MeshOf(road)));
                _wanted = new Wanted(body, [.. meshes], body is null ? [] : [.. boxes]);
            }
            catch (Exception e)
            {
                foreach ((_, _, Mesh mesh) in meshes) mesh.Dispose(Pool);
                _wanted = new Wanted(null, [], []);
                Log.Warn($"roads have no colliders: their meshes could not be built ({e.GetBaseException().Message})");
            }
        }
    }

    private static void Forget(ref Wanted? wanted)
    {
        foreach ((_, _, Mesh mesh) in wanted?.Meshes ?? []) mesh.Dispose(Pool);
        wanted = null;
    }

    private static Mesh MeshOf(RoadCollider road)
    {
        Pool.Take(road.Triangles, out Buffer<Triangle> triangles);
        for (int t = 0; t < road.Triangles; t++)
        {
            triangles[t] = new Triangle(Corner(road, 3 * t), Corner(road, (3 * t) + 1), Corner(road, (3 * t) + 2));
        }

        try
        {
            return new Mesh(triangles, Vector3.One, Pool);
        }
        catch
        {
            Pool.Return(ref triangles);
            throw;
        }
    }

    private static Vector3 Corner(RoadCollider road, int corner) => new(road.Corners[corner].X, road.Corners[corner].Y, road.Corners[corner].Z);

    /// <summary>A box as the physics wants it: its centre, and its axes as a body-fixed rotation.</summary>
    public static (double3 Centre, doubleQuat Orientation, double3 Size) Place(double3 centreCcf, double3 xCcf, double3 yCcf, double3 zCcf, double3 size)
        => (centreCcf, doubleQuat.CreateFromRotationMatrix(new double4x4(xCcf.X, xCcf.Y, xCcf.Z, 0.0, yCcf.X, yCcf.Y, yCcf.Z, 0.0,
                                                                         zCcf.X, zCcf.Y, zCcf.Z, 0.0, 0.0, 0.0, 0.0, 1.0)), size);

    // Main thread, between vehicle steps: the one moment the shared shape registry can be written, and
    // one in which no simulation is syncing, so a laying nobody holds here is held by nobody.
    private static void BetweenVehicleSteps()
    {
        if (Volatile.Read(ref _wanted) is null && Replaced.Count == 0) return;

        try
        {
            using ShapesUnlock unlocked = ConstraintSim.UnlockShapes();
            lock (Gate)
            {
                if (_wanted is { } wanted) Enter(unlocked.Shapes, wanted);

                int freed = 0;
                for (int k = Replaced.Count - 1; k >= 0; k--)
                {
                    if (Volatile.Read(ref Replaced[k].Holders) > 0) continue;
                    foreach (TypedIndex mesh in Replaced[k].Meshes) unlocked.Shapes.RemoveAndDispose(mesh, Pool);
                    freed += Replaced[k].Meshes.Length;
                    Replaced.RemoveAt(k);
                }
                if (freed > 0) Log.Info($"road colliders: {freed} mesh(es) of a laying no bubble holds freed, {Replaced.Count} laying(s) still held");
            }
        }
        catch (Exception e)
        {
            Complain($"could not change the road colliders' shapes yet, will try again: {e.GetBaseException().Message}");
        }
    }

    private static void Enter(Shapes shapes, Wanted wanted)
    {
        List<TypedIndex> meshes = [];
        try
        {
            List<Solid> solids = [];
            foreach ((double3 at, double radius, Mesh mesh) in wanted.Meshes)
            {
                meshes.Add(shapes.Add(mesh));
                solids.Add(new Solid(at, radius, Quaternion.Identity, meshes[^1]));
            }
            foreach ((double3 centre, doubleQuat orientation, double3 size) in wanted.Boxes)
            {
                solids.Add(new Solid(centre, 0.5 * size.Length(), ToBepu(orientation), ShapeOf(shapes, size)));
            }

            if (_laying is { } before) Replaced.Add(before);
            _laying = new Laying(wanted.Body, [.. solids], [.. meshes]);
            _wanted = null;
            Log.Info($"road colliders: {meshes.Count} mesh(es) of {wanted.Meshes.Sum(m => m.Mesh.Triangles.Length)} triangle(s) and {wanted.Boxes.Length} box(es), "
                   + $"{Pool.GetTotalAllocatedByteCount() / 1024} KiB held for meshes, {Replaced.Count} laying(s) waiting to be freed");
        }
        catch
        {
            // The meshes stay the wanted's, to be entered again.
            foreach (TypedIndex mesh in meshes) shapes.Remove(mesh);
            throw;
        }
    }

    // A box's shape is shared by every box of its size and never freed.
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
            foreach ((BodyHandle handle, VehicleUpdateState state) in __instance.HandleToState)
            {
                if (Buggies.HullMargins.TryGetValue(state.ReadOnlyVehicle, out float margin))
                {
                    __instance.Simulation.Bodies[handle].Collidable.MaximumSpeculativeMargin = margin;
                }
            }
        }
        catch (Exception e)
        {
            Complain($"the road colliders could not be synced: {e.GetBaseException().Message}");
        }
    }

    private static void Sync(ConstraintSim sim)
    {
        Laying? laying = _laying;
        SimState state = States.GetOrCreateValue(sim);

        // A simulation with no craft in it has no origin to pose a solid from, and nothing to meet one.
        bool here = TryOrigin(sim, out bool ccf, out IParentBody? parent, out double3 bub)
                    && laying is { Solids.Length: > 0 } && ccf && ReferenceEquals(parent, laying.Body);
        if (!ReferenceEquals(state.Laying, here ? laying : null))
        {
            Clear(sim, state);
            if (here)
            {
                state.Laying = laying;
                state.Handles = new StaticHandle[laying!.Solids.Length];
                state.Keys = new object?[laying.Solids.Length];
                Interlocked.Increment(ref laying.Holders);
            }
        }
        if (!here) return;

        IDictionary? registered = sim.ClutterStatics is { } clutter ? _clutterStatics?.GetValue(clutter) as IDictionary : null;
        bool moved = state.Bub != bub;
        // Rare, and the one moment every solid in the bubble is posed afresh: worth a line when a car
        // is thrown, to see whether it was then.
        if (moved && state.Laying is not null && (state.Bub - bub).Length() > 1.0)
        {
            Log.Info($"road colliders: a bubble's origin moved {(state.Bub - bub).Length():F0} m");
        }
        int first = -1, last = -1;
        for (int k = 0; k < laying!.Solids.Length; k++)
        {
            Solid solid = laying.Solids[k];
            bool held = state.Keys[k] is not null;
            bool near = (solid.AtCcf - bub).Length() < solid.RadiusM + ReachM + (held ? KeepM : 0.0);
            if (near && !held)
            {
                state.Handles[k] = sim.Simulation.Statics.Add(Describe(solid, bub), ref KSA.StaticsShouldntAwakenBodies.Shared);
                state.Keys[k] = state.Handles[k];
                if (registered is not null) registered[state.Keys[k]!] = _solidClutter;
                Interlocked.Increment(ref Adds);
            }
            else if (!near && held)
            {
                registered?.Remove(state.Keys[k]!);
                sim.Simulation.Statics.Remove(state.Handles[k]);
                state.Keys[k] = null;
            }
            else if (held && moved)
            {
                sim.Simulation.Statics.ApplyDescription(state.Handles[k], Describe(solid, bub), ref KSA.StaticsShouldntAwakenBodies.Shared);
            }

            if (!near) continue;
            if (first < 0) first = k;
            last = k;
        }
        state.Bub = bub;

        // The engine takes its own clutter out of that dictionary one at a time and everything out
        // at once, so with the first and the last of these still in it, all of them are.
        if (registered is not null && first >= 0 && !(registered.Contains(state.Keys[first]!) && registered.Contains(state.Keys[last]!)))
        {
            foreach (object? key in state.Keys)
            {
                if (key is not null) registered[key] = _solidClutter;
            }
        }
    }

    private static StaticDescription Describe(Solid solid, double3 bub)
    {
        double3 at = solid.AtCcf - bub;
        return new StaticDescription { Pose = new RigidPose(new Vector3((float)at.X, (float)at.Y, (float)at.Z), solid.Orientation), Shape = solid.Shape };
    }

    private static void Clear(ConstraintSim sim, SimState state)
    {
        IDictionary? registered = sim.ClutterStatics is { } clutter ? _clutterStatics?.GetValue(clutter) as IDictionary : null;
        for (int k = 0; k < state.Keys.Length; k++)
        {
            if (state.Keys[k] is not { } key) continue;
            registered?.Remove(key);
            sim.Simulation.Statics.Remove(state.Handles[k]);
        }
        Let(state);
    }

    // Counts a simulation off the laying it held, whose statics are out of it or gone with it.
    private static void Let(SimState state)
    {
        if (Interlocked.Exchange(ref state.Laying, null) is { } laying) Interlocked.Decrement(ref laying.Holders);
        state.Handles = [];
        state.Keys = [];
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
    private static void BeforeRecycle(ConstraintSim __instance)
    {
        if (!States.TryGetValue(__instance, out SimState? state)) return;
        Let(state);
        States.Remove(__instance);
    }

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
