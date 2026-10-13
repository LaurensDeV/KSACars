using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// A laid course's start, drawn on its asphalt: <see cref="StartGrid"/>'s chequered line and grid
/// boxes in one <see cref="RuntimeMesh"/>, written once a laying and handed to KSA's renderer with
/// the roads.
///
/// <para>It is paint and nothing else: no wheel and no physics asks it, and one that cannot be drawn
/// leaves the roads as they are.</para>
/// </summary>
internal static class StartLine
{
    private const string Material = "KSACars_RoadPaint_Material";

    // Room for the widest road's chequers and a grid, in quads.
    private const int MostQuads = 256;

    // A corner any car that ships turns inside, for the route the start is found on.
    private const double TurnRadiusM = 8.0;

    // Frames a laying has to have stood before its start is painted: a dragged road is laid again every frame.
    private const int SettledFrames = 30;

    private sealed record Shown(Celestial Body, double3 Origin);

    private static RuntimeMesh? _mesh;
    private static volatile Shown? _shown;
    private static string? _stopped;
    private static int _seen = -1, _painted = -1, _frames;

    public static bool Any => _shown is not null;

    /// <summary>Once a frame, from the frame hook: it writes a mesh. Never throws.</summary>
    public static void Update()
    {
        try
        {
            if (_stopped is not null) return;
            int generation = Roads.Generation;
            if (generation != _seen)
            {
                (_seen, _frames) = (generation, 0);
                Hide();
                return;
            }
            if (generation == _painted || ++_frames < SettledFrames) return;
            _painted = generation;
            if (!Roads.Any) return;

            if (Roads.RouteOver(null, 0.0, TurnRadiusM, out Celestial? body, out RoadSurface? surface, out _) is not { } route || body is null) return;
            if (StartGrid.Lay(route, surface) is not { } start || Mesh() is not { } mesh) return;
            if (start.Indices.Length > MostQuads * 6)
            {
                Log.Info($"start: not painted, {start.Indices.Length / 6} quads are more than the {MostQuads} there is room for");
                return;
            }
            if (!mesh.Upload(new RuntimeMesh.Content(start.Positions, start.Normals, start.Uvs, start.Indices, [start.Indices.Length], start.RadiusM), out string why))
            {
                Stop($"the start could not be written: {why}");
                return;
            }
            _shown = new Shown(body, start.Origin);
            Log.Info($"start: painted in {start.Indices.Length / 6} quads, {StartGrid.BoxesOn(route)} boxes, on a road {2.0 * route.Samples[0].HalfWidth:F1} m wide");
        }
        catch (Exception e)
        {
            Stop($"the start threw: {e.GetBaseException().GetType().Name}: {e.GetBaseException().Message}");
        }
    }

    /// <summary>Has no start drawn again this session. Never throws.</summary>
    public static void Stop(string why)
    {
        _shown = null;
        if (_stopped is not null) return;
        _stopped = why;
        Log.Warn(why);
    }

    // Inside the engine's render: called through a hook that catches whatever this throws.
    public static void Draw(IViewport viewport)
    {
        if (_shown is not { } shown || _mesh is not { } mesh || mesh.PartIndices(0) <= 0) return;

        doubleQuat turn = shown.Body.GetCcf2Cce();
        double3 at = shown.Body.GetPositionEcl() - viewport.GetCamera().PositionEcl + shown.Origin.Transform(turn);
        double3 x = new double3(1.0, 0.0, 0.0).Transform(turn), y = new double3(0.0, 1.0, 0.0).Transform(turn), z = new double3(0.0, 0.0, 1.0).Transform(turn);
        mesh.Parts[0].Transform = new float4x4((float)x.X, (float)x.Y, (float)x.Z, 0f, (float)y.X, (float)y.Y, (float)y.Z, 0f,
                                               (float)z.X, (float)z.Y, (float)z.Z, 0f, (float)at.X, (float)at.Y, (float)at.Z, 1f);
        mesh.Parts[0].Draw(Program.Instance.SuperMeshRenderSystem.ViewForViewport(viewport));
    }

    private static void Hide()
    {
        if (_shown is null) return;
        _shown = null;
        _mesh?.Empty();
    }

    // Room for a start, taken the first time one is painted and kept: KSA never gives it back.
    private static RuntimeMesh? Mesh()
    {
        if (_mesh is not null) return _mesh;
        int vertices = MostQuads * 4, indices = MostQuads * 6;
        if (RuntimeMesh.Reserve("KSACars_StartLine", vertices, indices, out string why) is not { } block
            || (_mesh = RuntimeMesh.Over(block, "KSACars_StartLine_Mesh", 0, vertices, 0, indices, [Material], out why)) is null)
        {
            Stop($"no room for the start: {why}");
        }
        return _mesh;
    }
}
