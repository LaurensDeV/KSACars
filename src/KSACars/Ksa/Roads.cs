using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// A circuit's roads on the ground: laid as one surface a run and one a junction, which a wheel asks
/// through <see cref="SurfaceOn"/>, and those surfaces as triangles, which <see cref="RoadDrawing"/>
/// draws and <see cref="RoadColliders"/> gives the physics, one solid for all that is joined, so a
/// hull, a kitten and any other craft meet what is drawn.
///
/// <para>The surface is swapped whole on every laying. The triangles are made again when a laying is
/// whole; while a road is being dragged only the runs the drag touches and the junctions they stop at
/// are, a few times a second, and the physics keeps what it had.</para>
/// </summary>
internal static class Roads
{
    // How often at most the meshes of a road being dragged are made and written again, and how much of
    // the time that may take: a long run is 60 ms to make, and each writing waits for the graphics card.
    private const double DragRedrawSeconds = 0.1, DragRedrawShare = 0.25;

    // How often the meshes with a place in the pool are chosen again, where there are more meshes than places.
    private const double RestockSeconds = 2.0;

    private sealed record Ribbon(double3[] SurfaceCcf, double HalfWidth, double LengthM, bool Closed);

    private sealed record Laid(Celestial Body, Ribbon[] Ribbons, RoadSurface Surface, Circuit Circuit, double LiftM, double SpacingM, RoadLoop[] Loops);

    private const string LoopKey = "loop:";

    private static volatile Laid? _laid;

    /// <summary>Whether a road is laid: one that is driven on, drawn or not.</summary>
    public static bool Any => _laid is not null;

    /// <summary>Whether the render hook has anything to hand over.</summary>
    public static bool AnyDrawn => RoadDrawing.Any || _patch is not null || RacingLine.Any;

    private static int _generation;

    /// <summary>
    /// Counts the times roads have been laid. What a wheel remembers of the road under it is of one
    /// laying: carried to the next, a road moved in the editor would take a car beside it along.
    /// </summary>
    public static int Generation => _generation;

    /// <summary>
    /// How far a point in <paramref name="body"/>'s own frame is above the road under it (m). Asked
    /// from the physics window, where nothing may throw: what is laid is swapped whole, never changed.
    /// </summary>
    public static bool TryHeightOver(Celestial body, double3 atCcf, double? last, out double metres)
    {
        metres = 0.0;
        return _laid is { } laid && ReferenceEquals(laid.Body, body) && laid.Surface.TryHeightOver(atCcf, last, out metres);
    }

    /// <summary>The circuit whose roads are laid on <paramref name="body"/>, or null with none.</summary>
    public static Circuit? CircuitOn(Celestial body) => _laid is { } laid && ReferenceEquals(laid.Body, body) ? laid.Circuit : null;

    /// <summary>The loops standing on a body, which a wheel is sprung against where it is on one. For the physics window.</summary>
    public static IReadOnlyList<RoadLoop> LoopsOn(Celestial body) => _laid is { } laid && ReferenceEquals(laid.Body, body) ? laid.Loops : [];

    /// <summary>The roads laid on <paramref name="body"/> as a wheel is over them, or null with none. Swapped whole, as above.</summary>
    public static RoadSurface? SurfaceOn(Celestial body) =>
        _laid is { } laid && ReferenceEquals(laid.Body, body) ? laid.Surface : null;

    /// <summary>
    /// A line to drive along the roads that are laid, through the circuit's points in
    /// <paramref name="through"/> or, with none, round from its first road; with the body they are on
    /// and their surface. Null with the reason. Not for the physics window: it reads the ground anew.
    /// </summary>
    public static Route? RouteOver(IReadOnlyList<int>? through, double offsetM, double turnRadiusM, out Celestial? body, out RoadSurface? surface,
                                   out string why, double raceInsideM = 0.0, IReadOnlyList<double>? nudgesM = null)
    {
        body = null;
        surface = null;
        if (_laid is not { } laid)
        {
            why = "no circuit is laid: lay one with road first";
            return null;
        }
        body = laid.Body;
        surface = laid.Surface;
        Celestial on = laid.Body;
        return Route.Of(laid.Circuit, on.GetDirCcfFromLatLon, on.MeanRadius, dir => on.GetTerrainHeightFromDirCcf(dir, accurate: true),
                        laid.LiftM, laid.SpacingM, through, offsetM, out why, turnRadiusM, raceInsideM, nudgesM);
    }

    /// <summary>
    /// How far this mod's reckoning of where KSA put each piece of clutter is from where KSA says it
    /// is, for the cells it has built colliders in: per kind, how many were compared and the mean and
    /// worst miss in metres, under each reading of the shader's arithmetic.
    /// </summary>
    public static Dictionary<string, object> ProbeClutter(Celestial body)
    {
        Dictionary<string, object> report = [];
        if (Program.GetPlanetRenderer()?.GroundClutterRenderer is not { } renderer) return report;
        if (!renderer.PlanetPhysicalData.TryGetValue(body.Hash, out ClutterEcotypePhysicalData[]? physical)) return report;
        if (!renderer.PlanetEcotypeRenderData.TryGetValue(body.Hash, out ClutterEcotypeRenderData[]? drawn)) return report;

        for (int e = 0; e < physical.Length && e < drawn.Length; e++)
        {
            if (physical[e] is not { } phys || drawn[e] is not { } ecotype) continue;
            int resolution = ecotype.CubeCellGrid.GridResolution, count = 0;
            double[] sum = new double[4], worst = new double[4];
            foreach (CubeCellGrid.Cell cell in phys.VesselGrid.loadedCells)
            {
                if (!phys.TryGetCellData(cell, out ReadOnlySpan<ClutterEcotypePhysicalData.CollisionData> instances)) continue;
                double3 anchor = phys.VesselGrid.GetCellAnchorDirection(cell);
                foreach (ref readonly ClutterEcotypePhysicalData.CollisionData instance in instances)
                {
                    double3 truth = Vec.Unit(anchor + double3.Unpack(in instance.CcfDirDelta));
                    count++;
                    for (int variant = 0; variant < 4; variant++)
                    {
                        double3 mine = ClutterGrid.Instance(cell.FaceId, cell.X, cell.Y, (int)instance.SubCellId, resolution,
                            jitter: (variant & 1) == 0, flip: (variant & 2) == 0);
                        double miss = Vec.Len(mine - truth) * body.MeanRadius;
                        sum[variant] += miss;
                        worst[variant] = Math.Max(worst[variant], miss);
                    }
                }
            }
            if (count == 0) continue;
            report[ecotype.EcotypeName] = new Dictionary<string, object>
            {
                ["compared"] = count, ["grid_resolution"] = resolution, ["physics_resolution"] = phys.VesselGrid.GridResolution,
                ["as_used"] = $"mean {sum[0] / count:F2} worst {worst[0]:F2}", ["worst_m"] = worst[0],
                ["no_jitter"] = $"mean {sum[1] / count:F2} worst {worst[1]:F2}",
                ["no_flip"] = $"mean {sum[2] / count:F2} worst {worst[2]:F2}",
                ["neither"] = $"mean {sum[3] / count:F2} worst {worst[3]:F2}",
            };
        }
        return report;
    }

    public static void Clear()
    {
        Interlocked.Increment(ref _generation);
        _laid = null;
        Refused = [];
        Warnings = [];
        _roadSolids = null;
        _pending = null;
        _status = [];
        HandColliders();
        RoadDrawing.Clear();
        _clutterMarginM = null;
        _clutterStale = true;
    }

    // The margin the laid roads are to be cleared of clutter by, or none to leave it standing; and
    // whether what KSA's masks say no longer matches that.
    private static double? _clutterMarginM;
    private static volatile bool _clutterStale;

    /// <summary>What the last change to the masks took, of each kind of clutter.</summary>
    public static Dictionary<string, int> ClutterTaken { get; private set; } = [];

    /// <summary>
    /// Has the grass, trees and rocks taken off the laid roads, <paramref name="marginM"/> past their
    /// edges. Asked for here and done by <see cref="SyncClutter"/>: KSA's physics workers read the masks
    /// while they run, and KSA itself only writes them while they are parked.
    /// </summary>
    public static void ClearClutter(double marginM)
    {
        _clutterMarginM = marginM;
        _clutterStale = true;
    }

    /// <summary>
    /// Brings KSA's clutter masks in step with the roads that are laid. Called from the physics
    /// window, which is where KSA changes them itself, and where nothing may throw.
    /// </summary>
    public static void SyncClutter()
    {
        if (!_clutterStale) return;
        _clutterStale = false;
        try
        {
            RestoreClutter();
            ClutterTaken = _clutterMarginM is { } margin ? TakeClutter(margin) : [];
        }
        catch (Exception e)
        {
            Log.Warn($"could not change what clutter stands on the roads: {e.Message}");
        }
    }

    // What this road cleared of each kind of clutter, so it can be put back: only the bits that were set.
    private static readonly List<(KeyHash Body, int Ecotype, CubeCellGrid.Cell Cell, uint[] Bits)> _cleared = [];

    // Takes the grass, trees and rocks off the laid roads through KSA's own per-cell mask, which is also
    // what its colliders are built from, and says how many it took of each kind.
    private static Dictionary<string, int> TakeClutter(double marginM)
    {
        Dictionary<string, int> taken = [];
        if (_laid is not { } road) return taken;
        if (Program.GetPlanetRenderer()?.GroundClutterRenderer is not { } renderer) return taken;
        KeyHash hash = road.Body.Hash;
        if (!renderer.PlanetEcotypeRenderData.TryGetValue(hash, out ClutterEcotypeRenderData[]? ecotypes)) return taken;

        foreach (Ribbon ribbon in road.Ribbons)
        {
            double3[] line = new double3[ribbon.SurfaceCcf.Length];
            for (int i = 0; i < line.Length; i++) line[i] = Vec.Unit(ribbon.SurfaceCcf[i]);

            for (int e = 0; e < ecotypes.Length; e++)
            {
                if (ecotypes[e] is not { } ecotype) continue;
                int count = 0;

                // Whatever is near enough to be looked at is asked of the roads' own outline: as wide as the road is
                // there, the shape a junction is, and with a bank's foot. Under a deck the ground is still ground, and
                // only what would stand up through the deck goes: all of it but the grass.
                // Everything but grass: KSA's shrubs are small trees, and stand through a deck two metres up as a tree does.
                bool tall = !ecotype.EcotypeName.Contains("Grass", StringComparison.OrdinalIgnoreCase);
                double near = marginM / road.Body.MeanRadius;
                bool Covered(double3 at)
                {
                    double3 east = Vec.Unit(Vec.Cross(Math.Abs(at.Z) < 0.9 ? new double3(0, 0, 1) : new double3(1, 0, 0), at)), north = Vec.Cross(at, east);
                    bool Under(double3 dir)
                    {
                        RoadSurface.Cover cover = road.Surface.Over(dir);
                        return cover == RoadSurface.Cover.Ground || (tall && cover == RoadSurface.Cover.Deck);
                    }

                    // Where it stands, and all round it at the margin: a road's edge is within the margin of it whichever
                    // way the road runs. A tree is as wide again, since what stands beside a deck comes up past its edge.
                    if (Under(at)) return true;
                    for (int ring = 1; ring <= (tall ? 2 : 1); ring++)
                    {
                        for (int k = 0; k < 8; k++)
                        {
                            double angle = k * Math.PI / 4.0, out_ = ring * near;
                            if (Under(Vec.Unit(at + (east * (Math.Cos(angle) * out_)) + (north * (Math.Sin(angle) * out_))))) return true;
                        }
                    }
                    return false;
                }

                foreach (((int face, int x, int y), uint[] bits) in
                         ClutterGrid.Under(line, road.Body.MeanRadius, (2.0 * ribbon.HalfWidth) + RoadRibbon.MostToeM + marginM,
                                           ecotype.CubeCellGrid.GridResolution, Covered))
                {
                    CubeCellGrid.Cell cell = new(x, y, face);
                    GroundClutterRenderer.ExclusionData mask = ecotype.PlacementData.GetExclusionData(cell);
                    for (int w = 0; w < bits.Length; w++)
                    {
                        bits[w] &= mask[w];
                        mask[w] &= ~bits[w];
                        count += System.Numerics.BitOperations.PopCount(bits[w]);
                    }
                    ecotype.PlacementData.ExcludeCell(cell, mask);
                    renderer.QueueExclusionUpload(hash, e, cell);
                    _cleared.Add((hash, e, cell, bits));
                }
                taken[ecotype.EcotypeName] = taken.GetValueOrDefault(ecotype.EcotypeName) + count;
            }
        }
        // The mask stops a collider being built; one already standing stays until KSA builds them all again.
        if (taken.Values.Any(n => n > 0)) KsaWorld.RebuildClutterColliders();
        CheckPlacement(road.Body);
        return taken;
    }

    /// <summary>
    /// What stands within <paramref name="withinM"/> of a place, of each kind of clutter: for each instance where it
    /// is, what of the roads is over it, and whether KSA's mask still has it. For the bridge, to see why one stands.
    /// </summary>
    public static List<Dictionary<string, object?>> ClutterNear(Celestial body, double3 atCcf, double withinM)
    {
        List<Dictionary<string, object?>> found = [];
        if (Program.GetPlanetRenderer()?.GroundClutterRenderer is not { } renderer) return found;
        if (!renderer.PlanetEcotypeRenderData.TryGetValue(body.Hash, out ClutterEcotypeRenderData[]? ecotypes)) return found;
        double3 here = Vec.Unit(atCcf);
        RoadSurface? surface = SurfaceOn(body);
        for (int e = 0; e < ecotypes.Length; e++)
        {
            if (ecotypes[e] is not { } ecotype || ecotype.EcotypeName.Contains("Grass", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (((int face, int x, int y), uint[] bits) in ClutterGrid.Under([here], body.MeanRadius, withinM, ecotype.CubeCellGrid.GridResolution))
            {
                GroundClutterRenderer.ExclusionData mask = ecotype.PlacementData.GetExclusionData(new CubeCellGrid.Cell(x, y, face));
                for (int slot = 0; slot < ClutterGrid.Slots; slot++)
                {
                    if ((bits[slot / 32] & (1u << (slot % 32))) == 0) continue;
                    double3 at = ClutterGrid.Instance(face, x, y, slot, ecotype.CubeCellGrid.GridResolution);
                    found.Add(new()
                    {
                        ["kind"] = ecotype.EcotypeName, ["index"] = e, ["away_m"] = Math.Round(Vec.Len(at - here) * body.MeanRadius, 1),
                        ["cover"] = surface?.Over(at).ToString() ?? "no roads", ["masked_out"] = (mask[slot / 32] & (1u << (slot % 32))) == 0,
                        ["resolution"] = ecotype.CubeCellGrid.GridResolution,
                    });
                }
            }
        }
        return found;
    }

    // Bodies whose clutter has been checked against KSA's own this session.
    private static readonly HashSet<KeyHash> PlacementChecked = [];

    // Where this reckons clutter stands is KSA's generation shader worked again by hand, and a change to
    // that shader moves every instance and says nothing. So once a body, when KSA has instances of its
    // own to compare with, the two are compared and the log says how they agree.
    private static void CheckPlacement(Celestial body)
    {
        if (PlacementChecked.Contains(body.Hash)) return;
        try
        {
            int compared = 0;
            double worst = 0.0;
            foreach ((_, object value) in ProbeClutter(body))
            {
                if (value is not Dictionary<string, object> kind) continue;
                compared += (int)kind["compared"];
                worst = Math.Max(worst, (double)kind["worst_m"]);
            }
            if (compared == 0) return;

            PlacementChecked.Add(body.Hash);
            if (worst > PlacementToleranceM)
            {
                Log.Warn($"clutter on {body.Id} is not where this reckons it: up to {worst:F2} m out over {compared} instance(s). KSA's placement "
                         + "has changed, and what is cleared under a road is not what stands on it: Sim/ClutterGrid.cs has to follow Generate.comp");
            }
            else
            {
                Log.Info($"clutter on {body.Id} is where this reckons it: {compared} instance(s) compared with KSA's, the worst {worst:F2} m out");
            }
        }
        catch (Exception e)
        {
            Log.Warn($"could not check where clutter stands against KSA's own: {e.Message}");
        }
    }

    // Further out than this and it is not rounding: a slot is at least this wide for every kind KSA ships.
    private const double PlacementToleranceM = 0.25;

    private static void RestoreClutter()
    {
        if (_cleared.Count == 0) return;
        GroundClutterRenderer? renderer = Program.GetPlanetRenderer()?.GroundClutterRenderer;
        foreach ((KeyHash body, int e, CubeCellGrid.Cell cell, uint[] bits) in _cleared)
        {
            if (renderer is null || !renderer.PlanetEcotypeRenderData.TryGetValue(body, out ClutterEcotypeRenderData[]? ecotypes)
                || e >= ecotypes.Length || ecotypes[e] is not { } ecotype) continue;
            GroundClutterRenderer.ExclusionData mask = ecotype.PlacementData.GetExclusionData(cell);
            for (int w = 0; w < bits.Length; w++) mask[w] |= bits[w];
            ecotype.PlacementData.ExcludeCell(cell, mask);
            renderer.QueueExclusionUpload(body, e, cell);
        }
        _cleared.Clear();
        KsaWorld.RebuildClutterColliders();
    }

    /// <summary>
    /// Lays a circuit's roads on <paramref name="body"/>, each on the ground along its whole length, and
    /// says how many points they took and the lowest and highest ground under them. For the frame
    /// hook: laid whole, it waits for the graphics card.
    /// </summary>
    /// <param name="whole">
    /// Whether everything is made again: every mesh drawn, and the physics' solids, which go into each
    /// bubble near them. A road being dragged about is laid without, every frame: the wheels' surface
    /// is new each time, the solids stay where they were, and of the meshes only those of the runs
    /// through <paramref name="touched"/> are made again, when <see cref="Update"/> next finds it time.
    /// </param>
    /// <param name="touched">The circuit's points a drag has moved the roads at.</param>
    public static (int Points, double LowM, double HighM) Lay(Celestial body, Circuit circuit, double liftM, double spacingM, bool whole,
                                                               IReadOnlyCollection<int>? touched = null)
    {
        _clutterMarginM = null;
        _clutterStale = true;
        int points = 0;
        double low = double.PositiveInfinity, high = double.NegativeInfinity;
        System.Diagnostics.Stopwatch took = System.Diagnostics.Stopwatch.StartNew();
        GroundLattice? lattice = null;
        if (!whole && CachesDragGround)
        {
            if (_lattice is null || !ReferenceEquals(_lattice.Body, body) || _lattice.Count > GroundLattice.MostPoints) _lattice = new GroundLattice(body);
            lattice = _lattice;
        }
        int before = lattice?.Reads ?? 0;
        double Ground(double3 dir)
        {
            double ground = lattice?.At(dir) ?? body.GetTerrainHeightFromDirCcf(dir, accurate: true);
            low = Math.Min(low, ground);
            high = Math.Max(high, ground);
            points++;
            return ground;
        }

        Interlocked.Increment(ref _generation);
        RoadLaying.Network network = RoadLaying.Laid(circuit, body.GetDirCcfFromLatLon, body.MeanRadius, Ground, liftM, spacingM);
        List<RoadLaying.Strip> strips = RoadLaying.Strips(network.Ribbons, Ground, spacingM);

        // What is cleared of clutter is a width either side of a line: a junction's asphalt as a line in
        // from each mouth to its point, as wide as the road and the rounding of its corners.
        List<Ribbon> lines = [.. strips.Select(s => new Ribbon(s.Line, s.HalfWidth, s.LengthM, s.Closed))];
        foreach (RoadJunction junction in network.Junctions)
        {
            foreach (RoadJunction.Arm arm in junction.Arms)
            {
                lines.Add(new Ribbon([junction.Point(arm.MouthAt), junction.Point(junction.At)], arm.HalfWidth + junction.RadiusM, arm.MouthS, false));
            }
        }
        // A loop stands where the roads at its two points end, so it is placed once they are laid.
        List<RoadLoop> loops = [];
        foreach (Circuit.Loop loop in circuit.Loops ?? [])
        {
            if (RoadLoop.Of(loop, network.Ribbons, out string noLoop) is { } placed) loops.Add(placed);
            else Log.Warn(noLoop);
        }
        _laid = strips.Count > 0 ? new Laid(body, [.. lines], RoadLaying.Surface(strips), circuit, liftM, spacingM, [.. loops]) : null;
        Refused = network.Refused;
        if (whole) Warnings = RoadWarning.Of(circuit, network, body.GetDirCcfFromLatLon);

        if (whole)
        {
            _pending = null;
            Mesh(body, network, null);
        }
        else
        {
            _pending = (body, network, [.. touched ?? []]);
        }
        LastLay = (took.Elapsed.TotalMilliseconds, lattice is null ? points : lattice.Reads - before);
        return (points, low, high);
    }

    private static (Celestial Body, RoadLaying.Network Network, HashSet<int> Touched)? _pending;

    /// <summary>How long the last laying took, its meshes apart, and how many times it read KSA's terrain.</summary>
    public static (double Ms, int TerrainReads) LastLay { get; private set; }

    /// <summary>How long the last making of a dragged road's meshes took.</summary>
    public static double LastDragMeshMs => 1000.0 * _redrawTook;

    /// <summary>Whether a drag reads the ground off a lattice of heights kept for it. The bridge clears it to compare.</summary>
    public static bool CachesDragGround { get; set; } = true;

    /// <summary>Whether a drag takes away the meshes of runs that are no longer runs. The bridge clears it to compare.</summary>
    public static bool TakesGoneRuns { get; set; } = true;

    /// <summary>How many runs have a mesh drawn that the roads last made into meshes do not have.</summary>
    public static int StaleRuns { get; private set; }

    private static GroundLattice? _lattice;

    /// <summary>Makes the meshes a drag has waiting now, whatever the time since the last: for the bridge to time.</summary>
    public static void MeshPending()
    {
        if (_pending is not { } pending) return;
        _pending = null;
        Mesh(pending.Body, pending.Network, pending.Touched);
        _redrawTook = SinceRedraw.Elapsed.TotalSeconds;
    }

    // The ground while a road is dragged, off heights kept on a 2 m lattice and read between its points.
    // A drag lays every road again each frame and KSA's accurate height is most of what that costs; the
    // chart the roads are laid on moves with the dragged point, so no two frames ask at the same place.
    // What is laid when the drag ends is read from KSA's own.
    private sealed class GroundLattice(Celestial body)
    {
        public const double StepM = 2.0;
        public const int MostPoints = 400_000;

        private readonly Dictionary<(int, int), double> _heights = [];
        private double3 _centre, _east, _north;
        private bool _set;

        public Celestial Body => body;
        public int Count => _heights.Count;
        public int Reads { get; private set; }

        public double At(double3 dir)
        {
            if (!_set)
            {
                _centre = Vec.Unit(dir);
                double3 pole = Math.Abs(_centre.Z) < 0.9 ? new double3(0, 0, 1) : new double3(1, 0, 0);
                _east = Vec.Unit(Vec.Cross(pole, _centre));
                _north = Vec.Cross(_centre, _east);
                _set = true;
            }

            // A flat sheet touching the body at the first place asked: a lattice on it is a lattice on the ground near there.
            double along = Vec.Dot(dir, _centre);
            double x = Vec.Dot(dir, _east) / along * body.MeanRadius / StepM, y = Vec.Dot(dir, _north) / along * body.MeanRadius / StepM;
            int i = (int)Math.Floor(x), j = (int)Math.Floor(y);
            double u = x - i, v = y - j;
            double low = Corner(i, j) + ((Corner(i + 1, j) - Corner(i, j)) * u), high = Corner(i, j + 1) + ((Corner(i + 1, j + 1) - Corner(i, j + 1)) * u);
            return low + ((high - low) * v);
        }

        private double Corner(int i, int j)
        {
            if (_heights.TryGetValue((i, j), out double height)) return height;
            double scale = StepM / body.MeanRadius;
            Reads++;
            return _heights[(i, j)] = body.GetTerrainHeightFromDirCcf(Vec.Unit(_centre + (_east * (i * scale)) + (_north * (j * scale))), accurate: true);
        }
    }

    /// <summary>
    /// The points of the circuit last laid where three or more roads meet and no junction could be
    /// made, each with the reason: for an editor to mark, on every laying and not only a whole one.
    /// </summary>
    public static IReadOnlyList<(int Node, string Why)> Refused { get; private set; } = [];

    /// <summary>What is wrong with the circuit last laid whole, for an editor to say and to mark.</summary>
    public static IReadOnlyList<RoadWarning> Warnings { get; private set; } = [];

    /// <summary>What the key of every junction mesh starts with, which tells it from a run's.</summary>
    public const string JunctionKey = "junctions ";

    // The junctions that were put in each mesh when the roads were last laid whole, by their points:
    // a drag draws a mesh again under the key it had, so it is of the same junctions.
    private static List<int[]> _junctionMeshes = [];
    private static readonly System.Diagnostics.Stopwatch SinceRedraw = System.Diagnostics.Stopwatch.StartNew();
    private static readonly System.Diagnostics.Stopwatch SinceRestock = System.Diagnostics.Stopwatch.StartNew();
    private static Dictionary<string, object?> _status = [];
    private static double _redrawTook;

    /// <summary>
    /// What the roads last laid whole came to: runs, meshes, vertices, the pool's places, what KSA's
    /// buffers have left, the physics' solids, and what went wrong with any of it.
    /// </summary>
    public static Dictionary<string, object?> Status()
    {
        Dictionary<string, object?> status = new(_status);
        foreach ((string key, object? value) in RoadDrawing.Report()) status[key] = value;
        return status;
    }

    /// <summary>
    /// Once a frame, from the frame hook and never from KSA's render: writes the meshes of a road
    /// being dragged when it is time to, and looks again at which meshes are drawn where the pool
    /// cannot hold them all. Never throws.
    /// </summary>
    public static void Update()
    {
        if (_pending is { } pending && SinceRedraw.Elapsed.TotalSeconds >= Math.Max(DragRedrawSeconds, _redrawTook / DragRedrawShare))
        {
            _pending = null;
            Mesh(pending.Body, pending.Network, pending.Touched);
            _redrawTook = SinceRedraw.Elapsed.TotalSeconds;
        }

        if (SinceRestock.Elapsed.TotalSeconds >= RestockSeconds && _laid is { } laid)
        {
            SinceRestock.Restart();
            if (EyeCcf(laid.Body) is { } eye) RoadDrawing.Restock(eye);
        }
    }

    // A run is told from the others by the points it goes through, in order.
    private static string KeyOf(RoadRibbon ribbon) => string.Join(",", ribbon.Spans.Select(s => $"{s.From}-{s.To}"));

    // The runs and the junctions as triangles, drawn and given to the physics; or, with the points a
    // drag touched, only the runs through those and the junctions they stop at, drawn in place of the
    // same ones before and the physics left as it was. A road whose triangles cannot be made or drawn
    // is still a surface under a wheel.
    private static void Mesh(Celestial body, RoadLaying.Network network, HashSet<int>? touched)
    {
        SinceRedraw.Restart();
        bool whole = touched is null;
        List<RoadDrawing.Run> runs = [];
        Dictionary<RoadRibbon, List<RoadMeshData>> made = new(ReferenceEqualityComparer.Instance);
        string? failed = null;

        // A drag can change which roads carry on through a point, and so which runs there are: a run
        // that was not drawn before is made whoever's it is, and one that is no longer a run is taken away.
        HashSet<string> shown = whole ? [] : RoadDrawing.ShownKeys();
        HashSet<string> gone = [.. shown.Where(k => !k.StartsWith(JunctionKey, StringComparison.Ordinal))];
        foreach (RoadRibbon ribbon in network.Ribbons)
        {
            gone.Remove(KeyOf(ribbon));
            if (touched is not null && shown.Contains(KeyOf(ribbon)) && !ribbon.Spans.Any(s => touched.Contains(s.From) || touched.Contains(s.To))) continue;
            try
            {
                List<RoadMeshData> meshes = RoadTessellation.Mesh(ribbon, RoadDrawList.Fit);
                runs.Add(new RoadDrawing.Run(KeyOf(ribbon), meshes));
                made[ribbon] = meshes;
            }
            catch (Exception e)
            {
                failed = $"the run through {KeyOf(ribbon)} could not be made into a mesh and is neither drawn nor solid: {e.GetBaseException().Message}";
                Log.Warn(failed);
            }
        }

        // A junction is made again where a run that stops at it was, or its point was touched.
        Dictionary<int, (RoadJunction Junction, RoadMeshData Mesh)> junctions = [];
        foreach (RoadJunction junction in network.Junctions)
        {
            if (touched is not null && !touched.Contains(junction.Node) && !junction.Arms.Any(a => a.Ribbon is { } r && made.ContainsKey(r))) continue;
            try
            {
                junctions[junction.Node] = (junction, RoadTessellation.Mesh(junction));
            }
            catch (Exception e)
            {
                failed = $"the junction at {junction.Node} could not be made into a mesh and is neither drawn nor solid: {e.GetBaseException().Message}";
                Log.Warn(failed);
            }
        }

        if (whole)
        {
            _junctionMeshes = [];
            foreach ((int[] nodes, RoadMeshData mesh) in RoadTessellation.Gather([.. junctions.Values], RoadDrawList.Fit))
            {
                _junctionMeshes.Add(nodes);
                runs.Add(new RoadDrawing.Run(JunctionKey + nodes[0], [mesh]));
            }
        }
        else
        {
            // Each mesh of junctions that has one of these in it, or had one that is no longer a
            // junction, whole and under its own key; and a junction that is new, on its own.
            HashSet<int> placed = [];
            foreach (int[] nodes in _junctionMeshes)
            {
                placed.UnionWith(nodes);
                if (!nodes.Any(n => junctions.ContainsKey(n) || touched!.Contains(n))) continue;
                List<(RoadJunction, RoadMeshData)> together = [];
                foreach (int node in nodes)
                {
                    try
                    {
                        if (junctions.TryGetValue(node, out (RoadJunction Junction, RoadMeshData Mesh) done)) together.Add(done);
                        else if (network.Junctions.FirstOrDefault(j => j.Node == node) is { } other) together.Add((other, RoadTessellation.Mesh(other)));
                    }
                    catch (Exception e)
                    {
                        Log.Warn($"the junction at {node} could not be made into a mesh: {e.GetBaseException().Message}");
                    }
                }
                runs.Add(new RoadDrawing.Run(JunctionKey + nodes[0], [.. RoadTessellation.Gather(together, RoadDrawList.Fit).Select(g => g.Mesh)]));
            }
            foreach ((int node, (_, RoadMeshData mesh)) in junctions)
            {
                if (!placed.Contains(node)) runs.Add(new RoadDrawing.Run(JunctionKey + node, [mesh]));
            }
        }

        // One solid for all that is joined: a hull crossing a mouth is on triangles that share an edge.
        List<RoadCollider> colliders = [];
        if (whole)
        {
            foreach (RoadLaying.Component component in RoadLaying.Components(network.Ribbons))
            {
                List<RoadMeshData> meshes = [.. component.Ribbons.Where(made.ContainsKey).SelectMany(r => made[r])];
                meshes.AddRange(component.Junctions.Where(j => junctions.ContainsKey(j.Node)).Select(j => junctions[j.Node].Mesh));
                if (RoadCollider.Of(meshes) is { } solid) colliders.Add(solid);
            }
            _roadSolids = colliders.Count > 0 ? (body, [.. colliders]) : null;
            HandColliders();
        }
        // A loop is drawn and is not solid: the wheels are sprung against it and nothing else of a car meets it.
        if (whole && _laid is { } laidNow)
        {
            for (int i = 0; i < laidNow.Loops.Length; i++)
            {
                runs.Add(new RoadDrawing.Run($"{LoopKey}{i}", [laidNow.Loops[i].Mesh(RoadDrawList.SlotVertices)]));
            }
        }
        foreach (string key in shown.Where(k => k.StartsWith(LoopKey, StringComparison.Ordinal))) gone.Remove(key);
        RoadDrawing.Show(body, runs, others: !whole, EyeCcf(body), TakesGoneRuns ? gone : null);
        HashSet<string> now = [.. network.Ribbons.Select(KeyOf)];
        StaleRuns = RoadDrawing.ShownKeys().Count(k => !k.StartsWith(JunctionKey, StringComparison.Ordinal) && !now.Contains(k));
        if (!whole) return;

        bool hooked = RoadColliders.Installed;
        _status = new()
        {
            ["junctions"] = network.Junctions.Count, ["junctions_refused"] = network.Refused.Count,
            ["collider_meshes"] = hooked ? colliders.Count : 0, ["collider_triangles"] = hooked ? colliders.Sum(c => c.Triangles) : 0,
            ["collider_most_triangles"] = hooked && colliders.Count > 0 ? colliders.Max(c => c.Triangles) : 0,
            ["collider_reach_m"] = colliders.Count > 0 ? Math.Round(colliders.Max(c => c.RadiusM)) : 0.0,
        };
        if (network.Refused.Count > 0)
        {
            _status["junction_warning"] = "no junction could be made, and the roads there lie over one another, at " + string.Join("; ", network.Refused.Select(r => $"{r.Node}: {r.Why}"));
            Log.Warn((string)_status["junction_warning"]!);
        }
        if (!hooked) _status["collider_warning"] = "the road colliders are not hooked: only wheels meet a road";
        if (!RoadDrawHook.Installed) _status["hook_warning"] = "the render hook is not installed: no road is drawn";
        if (failed is not null) _status["mesh_warning"] = failed;
        Log.Info("roads laid: " + string.Join(", ", Status().Select(kv => $"{kv.Key} {kv.Value}")));
    }

    // Where the player's eye is in a body's own frame, or the flown craft with no camera to ask.
    private static double3? EyeCcf(Celestial body)
    {
        try
        {
            double3? ecl = Program.GetMainCamera()?.PositionEcl ?? (KsaWorld.ControlledVehicle is { } craft ? KsaWorld.PositionEcl(craft) : null);
            return ecl is { } at ? (at - body.GetPositionEcl()).Transform(body.GetCcf2Cce().Inverse()) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Has no road drawn until roads are next laid whole, and leaves them laid: a car on one is still on it.</summary>
    public static void StopDrawing(string why)
    {
        _patch = null;
        RoadDrawing.Stop(why);
    }

    private static (Celestial Body, RoadCollider[] Solids)? _roadSolids;
    private static (Celestial Body, (double3 Centre, doubleQuat Orientation, double3 Size)[] Boxes)? _testBox;

    // The physics is given the solids of one body: the roads', and the experiment's with them if it is there too.
    private static void HandColliders()
    {
        Celestial? body = _roadSolids?.Body ?? _testBox?.Body;
        bool box = _testBox is { } test && ReferenceEquals(test.Body, body);
        RoadColliders.Want(body, _roadSolids?.Solids ?? [], box ? _testBox!.Value.Boxes : []);
    }

    // Something drawn for an experiment: where its origin is, and its three axes.
    private sealed record Marker(Celestial Body, double3 AtCcf, double3 X, double3 Y, double3 Z);

    /// <summary>
    /// The collider experiment's one box, put into KSA's physics centred over a place on the ground
    /// with its top <paramref name="topM"/> above it. Nothing draws it. A size of nothing takes it away.
    /// </summary>
    public static void TestBox(Celestial body, double3 overCcf, double sizeM, double thickM, double topM)
    {
        if (!(sizeM > 0.0))
        {
            _testBox = null;
            HandColliders();
            return;
        }

        double3 up = Vec.Unit(overCcf);
        (double3 east, double3 north) = GodView.Compass(up, Vec.Unit(body.GetDirCcfFromLatLon(90.0, 0.0)));
        double top = body.MeanRadius + body.GetTerrainHeightFromDirCcf(up, accurate: true) + topM;
        double3 centre = up * (top - (0.5 * thickM));
        _testBox = (body, [RoadColliders.Place(centre, east, north, up, new double3(sizeM, sizeM, thickM))]);
        HandColliders();
    }

    private const string PatchMaterial = "KSACars_Road_Material";

    private static volatile Marker? _patch;
    private static RuntimeMesh? _patchMesh;
    private static string? _patchRefused;

    /// <summary>
    /// The runtime mesh experiment's one patch: a curved square made here, written into the slot
    /// reserved for it and drawn level over a place on the ground, <paramref name="aboveM"/> up.
    /// Asked again it is written over in place; a size of nothing stops it being drawn. Answers what
    /// was reserved, what KSA's buffers have free and what went wrong. For the frame hook: it waits
    /// for the graphics card.
    /// </summary>
    public static Dictionary<string, object?> TestMesh(Celestial body, double3 overCcf, double sizeM, double aboveM, double bendM, int cells)
    {
        Dictionary<string, object?> report = [];
        if (!(sizeM > 0.0))
        {
            _patch = null;
            report["drawn"] = false;
            report["free"] = RuntimeMesh.Free()?.ToString();
            return report;
        }

        // Reserved once, and a refusal kept: KSA never gives back room that was taken on the way to one.
        if (_patchMesh is null && _patchRefused is null)
        {
            const string name = "KSACars_RuntimePatch";
            int vertices = MeshPatch.VerticesFor(MeshPatch.MaxCells), indices = MeshPatch.IndicesFor(MeshPatch.MaxCells);
            _patchMesh = RuntimeMesh.Reserve(name, vertices, indices, out string refused) is { } room
                ? RuntimeMesh.Over(room, name, 0, vertices, 0, indices, [PatchMaterial], out refused)
                : null;
            if (_patchMesh is null)
            {
                _patchRefused = refused;
                Log.Warn($"the runtime mesh cannot be drawn: {refused}");
            }
        }
        if (_patchMesh is not { } mesh)
        {
            report["error"] = _patchRefused;
            report["free"] = RuntimeMesh.Free()?.ToString();
            return report;
        }

        MeshPatch patch = MeshPatch.Build(sizeM, cells, bendM);
        bool uploaded = mesh.Upload(new RuntimeMesh.Content(patch.Positions, patch.Normals, patch.Uvs, patch.Indices, [patch.Indices.Length],
                                                            patch.Radius()), out string why);
        report["reserved_vertices"] = mesh.VertexCapacity;
        report["reserved_indices"] = mesh.IndexCapacity;
        report["vertex_offset"] = mesh.VertexOffset;
        report["index_offset"] = mesh.IndexOffset;
        report["free_before_reserving"] = mesh.FreeBefore.ToString();
        report["free_after_reserving"] = mesh.FreeAfter.ToString();
        report["free"] = RuntimeMesh.Free()?.ToString();
        report["vertices"] = mesh.Vertices;
        report["indices"] = mesh.Indices;
        report["drawn"] = uploaded;
        if (!uploaded)
        {
            _patch = null;
            report["error"] = why;
            return report;
        }

        // The patch's own frame is right-handed, so across is north's left: a left-handed one would
        // turn every triangle's back to the eye.
        double3 up = Vec.Unit(overCcf);
        (_, double3 north) = GodView.Compass(up, Vec.Unit(body.GetDirCcfFromLatLon(90.0, 0.0)));
        double radius = body.MeanRadius + body.GetTerrainHeightFromDirCcf(up, accurate: true) + aboveM;
        _patch = new Marker(body, (up * radius) - (north * (0.5 * sizeM)), Vec.Cross(up, north), up, north);
        return report;
    }

    private static void DrawAt(StaticMeshRenderable mesh, Marker place, IViewport viewport)
    {
        doubleQuat turn = place.Body.GetCcf2Cce();
        double3 at = place.Body.GetPositionEcl() - viewport.GetCamera().PositionEcl + place.AtCcf.Transform(turn);
        double3 x = place.X.Transform(turn), y = place.Y.Transform(turn), z = place.Z.Transform(turn);
        mesh.Transform = new float4x4((float)x.X, (float)x.Y, (float)x.Z, 0f, (float)y.X, (float)y.Y, (float)y.Z, 0f,
                                      (float)z.X, (float)z.Y, (float)z.Z, 0f, (float)at.X, (float)at.Y, (float)at.Z, 1f);
        mesh.Draw(Program.Instance.SuperMeshRenderSystem.ViewForViewport(viewport));
    }

    // Inside the engine's render: called through a hook that catches whatever this throws.
    public static void Draw(IViewport viewport)
    {
        if (_patch is { } patch && _patchMesh is { } runtime) DrawAt(runtime.Renderable, patch, viewport);
        RoadDrawing.Draw(viewport);

        // On its own, so a line that cannot be drawn leaves the roads drawn.
        try
        {
            RacingLine.Draw(viewport);
        }
        catch (Exception e)
        {
            RacingLine.Stop($"drawing the racing line threw: {e.GetBaseException().GetType().Name}: {e.GetBaseException().Message}");
        }
    }
}
