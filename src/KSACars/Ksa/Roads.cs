using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// A circuit's roads on the ground, drawn as slabs: one box for each stretch between two points of a
/// road, as wide as the road, <see cref="ThicknessM"/> deep and with its top on the road's line.
///
/// <para>The roads are held in the body's own frame and handed over each frame from the camera, as
/// <see cref="RoadDrawHook"/> is called.</para>
/// </summary>
internal static class Roads
{
    /// <summary>How deep a road's slab is. Laid on the ground most of it is under it; raised, it is the deck's edge.</summary>
    public const double ThicknessM = 0.3;

    private const double DrawWithinM = 8_000.0;

    private sealed record Ribbon(double3[] SurfaceCcf, double HalfWidth, double LengthM, bool Closed);

    private sealed record Laid(Celestial Body, Ribbon[] Ribbons, RoadSurface Surface);

    private static volatile Laid? _laid;
    private static double3[] _ego = [];

    public static bool Any => _laid is not null || _marker is not null;

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
                ["as_used"] = $"mean {sum[0] / count:F2} worst {worst[0]:F2}",
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
                foreach (((int face, int x, int y), uint[] bits) in
                         ClutterGrid.Under(line, road.Body.MeanRadius, ribbon.HalfWidth + marginM, ecotype.CubeCellGrid.GridResolution))
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
        return taken;
    }

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
    /// says how many points they took and the lowest and highest ground under them.
    /// </summary>
    public static (int Points, double LowM, double HighM) Lay(Celestial body, Circuit circuit, double liftM, double spacingM)
    {
        _clutterMarginM = null;
        _clutterStale = true;
        int points = 0;
        double low = double.PositiveInfinity, high = double.NegativeInfinity;
        double Ground(double3 dir)
        {
            double ground = body.GetTerrainHeightFromDirCcf(dir, accurate: true);
            low = Math.Min(low, ground);
            high = Math.Max(high, ground);
            points++;
            return ground;
        }

        Interlocked.Increment(ref _generation);
        List<RoadLaying.Strip> strips = RoadLaying.Lay(circuit, body.GetDirCcfFromLatLon, body.MeanRadius, Ground, liftM, spacingM);
        _laid = strips.Count > 0
            ? new Laid(body, [.. strips.Select(s => new Ribbon(s.Line, s.HalfWidth, s.LengthM, s.Closed))], RoadLaying.Surface(strips))
            : null;
        return (points, low, high);
    }

    // A box drawn for the collider experiment: where its top face's near edge is, and its three edges.
    private sealed record Marker(Celestial Body, double3 AtCcf, double3 X, double3 Y, double3 Z);

    private static volatile Marker? _marker;

    /// <summary>
    /// The collider experiment's one box: put into KSA's physics and drawn, centred over a place on the
    /// ground with its top <paramref name="topM"/> above it. A size of nothing takes it away.
    /// </summary>
    public static void TestBox(Celestial body, double3 overCcf, double sizeM, double thickM, double topM)
    {
        if (!(sizeM > 0.0))
        {
            _marker = null;
            RoadColliders.Want(null, []);
            return;
        }

        double3 up = Vec.Unit(overCcf);
        (double3 east, double3 north) = GodView.Compass(up, Vec.Unit(body.GetDirCcfFromLatLon(90.0, 0.0)));
        double top = body.MeanRadius + body.GetTerrainHeightFromDirCcf(up, accurate: true) + topM;
        double3 centre = up * (top - (0.5 * thickM));
        RoadColliders.Want(body, [RoadColliders.Place(centre, east, north, up, new double3(sizeM, sizeM, thickM))]);
        _marker = new Marker(body, (up * top) - (north * (0.5 * sizeM)), east * sizeM, up * thickM, north * sizeM);
    }

    // Inside the engine's render: called through a hook that catches whatever this throws.
    public static void Draw(IViewport viewport)
    {
        if (_marker is { } marker && RoadMesh.Slab is { } box)
        {
            doubleQuat turn = marker.Body.GetCcf2Cce();
            double3 at = marker.Body.GetPositionEcl() - viewport.GetCamera().PositionEcl + marker.AtCcf.Transform(turn);
            double3 x = marker.X.Transform(turn), y = marker.Y.Transform(turn), z = marker.Z.Transform(turn);
            box.Transform = new float4x4((float)x.X, (float)x.Y, (float)x.Z, 0f, (float)y.X, (float)y.Y, (float)y.Z, 0f,
                                         (float)z.X, (float)z.Y, (float)z.Z, 0f, (float)at.X, (float)at.Y, (float)at.Z, 1f);
            box.Draw(Program.Instance.SuperMeshRenderSystem.ViewForViewport(viewport));
        }

        if (_laid is not { } laid || RoadMesh.Slab is not { } slab) return;

        Camera camera = viewport.GetCamera();
        doubleQuat ccf2Cce = laid.Body.GetCcf2Cce();
        double3 bodyEgo = laid.Body.GetPositionEcl() - camera.PositionEcl;
        var view = Program.Instance.SuperMeshRenderSystem.ViewForViewport(viewport);

        foreach (Ribbon road in laid.Ribbons)
        {
            double3[] line = road.SurfaceCcf;
            if (Vec.Len(bodyEgo + line[line.Length / 2].Transform(ccf2Cce)) > DrawWithinM + road.LengthM) continue;

            if (_ego.Length < line.Length) _ego = new double3[line.Length];
            for (int i = 0; i < line.Length; i++) _ego[i] = bodyEgo + line[i].Transform(ccf2Cce);

            double width = 2.0 * road.HalfWidth;
            double reachBefore = 0.0;
            for (int i = 0; i < line.Length - 1; i++)
            {
                double3 along = _ego[i + 1] - _ego[i];
                double length = Vec.Len(along);
                if (!(length > 1e-6)) continue;
                double3 ahead = along / length;

                double3 up = Vec.Unit(line[i]).Transform(ccf2Cce);

                // Boxes end square, so on a bend each reaches past its end far enough to close the
                // wedge that would open on the outside between it and the next. Only for a turn seen
                // from above: over a crest or into a dip two boxes already share the edge between them,
                // and one reaching on along its own slope would stand proud of the next.
                // A ring's last box is followed by its first, and its first is reached back by the same.
                double reachAfter = 0.0;
                int after = i + 2 < line.Length ? i + 2 : road.Closed ? 1 : -1;
                if (after >= 0)
                {
                    double3 next = Vec.Unit(_ego[after] - _ego[after - 1]);
                    reachAfter = road.HalfWidth * Math.Abs(Vec.Dot(Vec.Cross(ahead, next), up));
                }
                if (i == 0 && road.Closed)
                {
                    double3 before = Vec.Unit(_ego[line.Length - 1] - _ego[line.Length - 2]);
                    reachBefore = road.HalfWidth * Math.Abs(Vec.Dot(Vec.Cross(before, ahead), up));
                }

                if (Vec.Len(_ego[i]) <= DrawWithinM)
                {
                    double3 across = Vec.Unit(Vec.Cross(up, ahead));
                    double3 x = across * width;
                    double3 y = Vec.Cross(ahead, across) * ThicknessM;
                    double3 z = ahead * (length + reachBefore + reachAfter);
                    double3 at = _ego[i] - (ahead * reachBefore);
                    slab.Transform = new float4x4((float)x.X, (float)x.Y, (float)x.Z, 0f,
                                                  (float)y.X, (float)y.Y, (float)y.Z, 0f,
                                                  (float)z.X, (float)z.Y, (float)z.Z, 0f,
                                                  (float)at.X, (float)at.Y, (float)at.Z, 1f);
                    slab.Draw(view);
                }
                reachBefore = reachAfter;
            }
        }
    }
}
