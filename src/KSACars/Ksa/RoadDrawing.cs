using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// The roads as they are drawn: each mesh of a laid circuit written into a place of its own in a pool
/// of <see cref="RuntimeMesh"/>es, and handed to KSA's renderer for each view from
/// <see cref="RoadDrawHook"/>, once in asphalt and once in earth.
///
/// <para>KSA never gives back room in its mesh buffers, so the pool only grows, a block at a time up
/// to <see cref="RoadDrawList.MostSlots"/> places, and a place whose mesh is gone is kept for the
/// next. A circuit with more meshes than there are places has the ones nearest the eye drawn, and
/// which those are is looked at again as the eye moves.</para>
///
/// <para>Nothing here is asked by a wheel or by the physics: a road that cannot be drawn is still
/// driven on.</para>
/// </summary>
internal static class RoadDrawing
{
    private const string LinedMaterial = "KSACars_RoadLined_Material", AsphaltMaterial = "KSACars_Road_Material", EarthMaterial = "KSACars_RoadEarth_Material",
                         TrimMaterial = "KSACars_RoadTrim_Material";

    private const double DrawWithinM = 8_000.0;

    // An undrawn mesh takes a drawn one's place when it is this much the nearer, so two a like distance off do not swap every look.
    private const double SwapNearerM = 150.0;

    /// <summary>One run of roads, or the junctions that share a mesh: what tells it from the others, and its meshes.</summary>
    public sealed record Run(string Key, IReadOnlyList<RoadMeshData> Meshes);

    // A mesh and the place it is written in, or -1 while it has none and is not drawn.
    private sealed record Piece(string Key, RoadMeshData Mesh, int Slot);

    private sealed record Shown(Celestial Body, Piece[] Pieces);

    private static readonly List<RuntimeMesh> Slots = [];
    private static readonly SlotLedger Ledger = new();

    private static volatile Shown? _shown;
    private static string? _noMoreRoom, _stopped, _warned;
    private static int _blocks, _uploadedVertices, _uploadedIndices, _leftOut;

    private static double[] _range = [];
    private static int[] _draws = [], _picked = [];

    public static bool Any => _shown is not null;

    /// <summary>The keys of the runs that have a mesh drawn.</summary>
    public static HashSet<string> ShownKeys() => [.. (_shown?.Pieces ?? []).Select(p => p.Key)];

    /// <summary>
    /// Has these runs drawn on <paramref name="body"/>: in place of everything drawn, or with
    /// <paramref name="others"/> in place of only the runs before that had the same keys. For the
    /// frame hook, since it waits for the graphics card. Never throws.
    /// </summary>
    /// <param name="eyeCcf">Where the nearest meshes are reckoned from when there are more than places, in the body's own frame.</param>
    /// <param name="gone">Keys whose meshes are taken away though nothing replaces them: runs that are runs no longer.</param>
    public static void Show(Celestial body, IReadOnlyList<Run> runs, bool others, double3? eyeCcf, IReadOnlySet<string>? gone = null)
    {
        try
        {
            Shown? before = _shown is { } was && ReferenceEquals(was.Body, body) ? was : null;
            if (!others) (_stopped, _warned) = (null, null);
            if (_stopped is not null) return;

            HashSet<string> replaced = [.. runs.Select(r => r.Key)];
            List<Piece> pieces = [];
            foreach (Piece piece in _shown?.Pieces ?? [])
            {
                if (others && before is not null && !replaced.Contains(piece.Key) && gone?.Contains(piece.Key) != true) pieces.Add(piece);
                else Ledger.Give(piece.Slot);
            }
            int kept = pieces.Count;
            foreach (Run run in runs) pieces.AddRange(run.Meshes.Where(m => m.Indices.Length > 0).Select(m => new Piece(run.Key, m, -1)));

            Place(body, pieces, kept, eyeCcf);
        }
        catch (Exception e)
        {
            Stop($"showing the roads threw: {e.GetBaseException().GetType().Name}: {e.GetBaseException().Message}");
        }
    }

    // Gives the pieces from `from` on a place each, or the nearest of them as many as there are, and writes their meshes.
    private static void Place(Celestial body, List<Piece> pieces, int from, double3? eyeCcf)
    {
        int wanted = pieces.Count - from;
        while (Ledger.Free < wanted && Ledger.Size < RoadDrawList.MostSlots && _noMoreRoom is null) Grow();

        List<double> range = [];
        for (int i = from; i < pieces.Count; i++) range.Add(eyeCcf is { } eye ? Range(pieces[i].Mesh, eye) : i);

        List<(RuntimeMesh, RuntimeMesh.Content)> uploads = [];
        (_uploadedVertices, _uploadedIndices) = (0, 0);
        foreach (int nearest in RoadDrawList.Nearest(range, Ledger.Free))
        {
            int slot = Ledger.Take();
            if (slot < 0) break;
            Piece piece = pieces[from + nearest];
            pieces[from + nearest] = piece with { Slot = slot };
            uploads.Add((Slots[slot], ContentOf(piece.Mesh, !piece.Key.StartsWith(Roads.JunctionKey, StringComparison.Ordinal))));
            _uploadedVertices += piece.Mesh.Positions.Length;
            _uploadedIndices += piece.Mesh.Indices.Length;
        }

        _leftOut = pieces.Count(p => p.Slot < 0);
        if (_leftOut > 0 && _warned is null)
        {
            _warned = $"{_leftOut} of {pieces.Count} road meshes have no place and are not drawn: {Ledger.Size} places of "
                    + $"{RoadDrawList.SlotVertices} vertices, {RoadDrawList.MostSlots} at most"
                    + (_noMoreRoom is null ? "" : $"; no more could be had: {_noMoreRoom}");
            Log.Warn(_warned);
        }

        if (!RuntimeMesh.Upload(uploads, out string why))
        {
            foreach (Piece piece in pieces) Ledger.Give(piece.Slot);
            Stop($"the road meshes could not be uploaded: {why}");
            return;
        }
        _shown = pieces.Count > 0 ? new Shown(body, [.. pieces]) : null;
    }

    // A run's asphalt is drawn with its markings, a deck's sides and a junction's asphalt in plain asphalt, which
    // has no way along it, the verges and banks in earth, and kerbs and barriers in their own: four draws a mesh at most.
    private static RuntimeMesh.Content ContentOf(RoadMeshData mesh, bool lined)
    {
        int[] indices = RoadDrawList.ByMaterial(mesh, out int road, out int earth);
        int marked = lined ? mesh.AsphaltIndices : 0;
        return new RuntimeMesh.Content(mesh.Positions, mesh.Normals, mesh.Uvs, indices, [marked, road - marked, earth, mesh.TrimIndices], mesh.RadiusM);
    }

    private static double Range(RoadMeshData mesh, double3 eyeCcf) => RoadDrawList.Range(Vec.Len(mesh.Origin - eyeCcf), mesh.RadiusM);

    // One more block of places. A refusal is kept: KSA never gives back room that was taken on the way to one.
    private static void Grow()
    {
        int count = Math.Min(RoadDrawList.BlockSlots, RoadDrawList.MostSlots - Ledger.Size);
        string name = $"KSACars_Roads_{_blocks}";
        if (RuntimeMesh.Reserve(name, count * RoadDrawList.SlotVertices, count * RoadDrawList.SlotIndices, out string why) is not { } block)
        {
            _noMoreRoom = why;
            Log.Warn($"no more room for road meshes: {why}");
            return;
        }
        _blocks++;

        int made = 0;
        for (int i = 0; i < count; i++)
        {
            if (RuntimeMesh.Over(block, $"{name}_{i}", i * RoadDrawList.SlotVertices, RoadDrawList.SlotVertices, i * RoadDrawList.SlotIndices,
                                 RoadDrawList.SlotIndices, [LinedMaterial, AsphaltMaterial, EarthMaterial, TrimMaterial], out why) is not { } mesh)
            {
                _noMoreRoom = why;
                Log.Warn($"no more places for road meshes: {why}");
                break;
            }
            Slots.Add(mesh);
            made++;
        }
        Ledger.Grow(made);
    }

    /// <summary>
    /// Where there are more meshes than places, has the ones nearest <paramref name="eyeCcf"/> be the
    /// ones with a place. For the frame hook, now and then: it writes the meshes that change. Never throws.
    /// </summary>
    public static void Restock(double3 eyeCcf)
    {
        if (_leftOut == 0 || _stopped is not null || _shown is not { } shown) return;

        try
        {
            double furthestDrawn = 0.0, nearestUndrawn = double.PositiveInfinity;
            foreach (Piece piece in shown.Pieces)
            {
                double range = Range(piece.Mesh, eyeCcf);
                if (piece.Slot >= 0) furthestDrawn = Math.Max(furthestDrawn, range);
                else nearestUndrawn = Math.Min(nearestUndrawn, range);
            }
            if (!(nearestUndrawn + SwapNearerM < furthestDrawn)) return;

            // The furthest give up their places and everything without one is placed afresh, nearest first.
            List<Piece> pieces = [.. shown.Pieces.OrderBy(p => p.Slot < 0 ? 1 : 0)];
            int keep = pieces.Count(p => p.Slot >= 0);
            pieces.Sort(0, keep, Comparer<Piece>.Create((a, b) => Range(a.Mesh, eyeCcf).CompareTo(Range(b.Mesh, eyeCcf))));
            while (keep > 0 && Range(pieces[keep - 1].Mesh, eyeCcf) > nearestUndrawn + SwapNearerM)
            {
                keep--;
                Ledger.Give(pieces[keep].Slot);
                pieces[keep] = pieces[keep] with { Slot = -1 };
            }
            Place(shown.Body, pieces, keep, eyeCcf);
        }
        catch (Exception e)
        {
            Stop($"choosing the roads to draw threw: {e.GetBaseException().GetType().Name}: {e.GetBaseException().Message}");
        }
    }

    /// <summary>Has no road drawn, and every place free for the next.</summary>
    public static void Clear()
    {
        foreach (Piece piece in _shown?.Pieces ?? []) Ledger.Give(piece.Slot);
        _shown = null;
        (_stopped, _warned, _leftOut, _uploadedVertices, _uploadedIndices) = (null, null, 0, 0, 0);
    }

    /// <summary>Has no road drawn until roads are next shown whole, and says why once. Wheels and colliders are not touched.</summary>
    public static void Stop(string why)
    {
        foreach (Piece piece in _shown?.Pieces ?? []) Ledger.Give(piece.Slot);
        _shown = null;
        if (_stopped is null) Log.Warn($"roads are not drawn, and are still driven on: {why}");
        _stopped = why;
    }

    /// <summary>What is drawn and what room it takes, for the bridge and the log.</summary>
    public static Dictionary<string, object?> Report()
    {
        Piece[] pieces = _shown?.Pieces ?? [];
        Dictionary<string, object?> report = new()
        {
            ["drawn"] = _shown is not null, ["runs"] = pieces.Where(p => !p.Key.StartsWith(Roads.JunctionKey)).Select(p => p.Key).Distinct().Count(),
            ["junction_meshes"] = pieces.Count(p => p.Key.StartsWith(Roads.JunctionKey)), ["meshes"] = pieces.Length,
            ["meshes_not_drawn"] = _leftOut, ["vertices"] = pieces.Sum(p => p.Mesh.Positions.Length), ["indices"] = pieces.Sum(p => p.Mesh.Indices.Length),
            ["uploaded_vertices"] = _uploadedVertices, ["uploaded_indices"] = _uploadedIndices,
            ["slots_used"] = Ledger.Used, ["slots"] = Ledger.Size, ["slots_at_most"] = RoadDrawList.MostSlots,
            ["slot_vertices"] = RoadDrawList.SlotVertices, ["slot_indices"] = RoadDrawList.SlotIndices,
            ["ksa_buffers_free"] = RuntimeMesh.Free()?.ToString(),
        };
        if ((_stopped ?? _warned ?? _noMoreRoom) is { } warning) report["draw_warning"] = warning;
        return report;
    }

    // Inside the engine's render: called through a hook that catches whatever this throws.
    public static void Draw(IViewport viewport)
    {
        if (_shown is not { } shown) return;

        Piece[] pieces = shown.Pieces;
        if (_range.Length < pieces.Length) (_range, _draws, _picked) = (new double[pieces.Length], new int[pieces.Length], new int[pieces.Length]);

        // A mesh is in the body's own axes about its origin, so it is drawn turned as the body is and nothing else.
        doubleQuat turn = shown.Body.GetCcf2Cce();
        double3 bodyEgo = shown.Body.GetPositionEcl() - viewport.GetCamera().PositionEcl;
        double3 x = new double3(1.0, 0.0, 0.0).Transform(turn), y = new double3(0.0, 1.0, 0.0).Transform(turn), z = new double3(0.0, 0.0, 1.0).Transform(turn);

        for (int i = 0; i < pieces.Length; i++)
        {
            Piece piece = pieces[i];
            _draws[i] = 0;
            if (piece.Slot < 0) continue;
            RuntimeMesh mesh = Slots[piece.Slot];
            for (int p = 0; p < mesh.Parts.Length; p++) _draws[i] += mesh.PartIndices(p) > 0 ? 1 : 0;
            _range[i] = RoadDrawList.Range(Vec.Len(bodyEgo + piece.Mesh.Origin.Transform(turn)), piece.Mesh.RadiusM);
        }

        // KSA has so many draws a view, and the line to drive has some of them while it is drawn.
        int most = RoadDrawList.MostDraws - (RacingLine.Any ? RacingLine.MostDraws : 0);
        int count = RoadDrawList.Pick(_range.AsSpan(0, pieces.Length), _draws.AsSpan(0, pieces.Length), DrawWithinM, most, _picked, out _);
        var view = Program.Instance.SuperMeshRenderSystem.ViewForViewport(viewport);
        for (int k = 0; k < count; k++)
        {
            Piece piece = pieces[_picked[k]];
            RuntimeMesh mesh = Slots[piece.Slot];
            double3 at = bodyEgo + piece.Mesh.Origin.Transform(turn);
            float4x4 transform = new((float)x.X, (float)x.Y, (float)x.Z, 0f, (float)y.X, (float)y.Y, (float)y.Z, 0f,
                                     (float)z.X, (float)z.Y, (float)z.Z, 0f, (float)at.X, (float)at.Y, (float)at.Z, 1f);
            for (int p = 0; p < mesh.Parts.Length; p++)
            {
                if (mesh.PartIndices(p) <= 0) continue;
                mesh.Parts[p].Transform = transform;
                mesh.Parts[p].Draw(view);
            }
        }
    }
}
