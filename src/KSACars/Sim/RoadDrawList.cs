namespace KSACars;

/// <summary>
/// Which of a circuit's meshes are drawn: the ones that have room kept for them, and of those the
/// ones a view has draws for, the nearest first in both.
/// </summary>
internal static class RoadDrawList
{
    /// <summary>
    /// What one place in the pool holds, and so the most one mesh is cut to. A 100 m of road in
    /// hairpins is 2,200 vertices; a straight is a tenth of that, and a mesh of it is cut at
    /// <see cref="MostMeshM"/> instead.
    /// </summary>
    public const int SlotVertices = 2048, SlotIndices = 7168;

    public const double MostMeshM = 400.0;

    /// <summary>
    /// How many places the pool is grown by at a time, and the most it ever has: 114,688 vertices,
    /// a quarter of what the engine's buffers hold for every mesh in the game.
    /// </summary>
    public const int BlockSlots = 8, MostSlots = 56;

    /// <summary>The most draws roads take in a view, of the 256 the engine has for everything.</summary>
    public const int MostDraws = 200;

    public static readonly RoadTessellation.Fit Fit = new(SlotVertices, SlotIndices, MostMeshM);

    /// <summary>
    /// A mesh's triangles in the order they are drawn in: the asphalt with a deck's sides and
    /// underside, which are one material and one draw, and then the earth.
    /// </summary>
    public static int[] ByMaterial(RoadMeshData mesh, out int road, out int earth)
    {
        road = mesh.AsphaltIndices + mesh.DeckIndices;
        earth = mesh.EarthIndices;
        int[] indices = new int[mesh.Indices.Length];
        Array.Copy(mesh.Indices, 0, indices, 0, mesh.AsphaltIndices);
        Array.Copy(mesh.Indices, mesh.AsphaltIndices + mesh.EarthIndices, indices, mesh.AsphaltIndices, mesh.DeckIndices);
        Array.Copy(mesh.Indices, mesh.AsphaltIndices, indices, road, mesh.EarthIndices);

        // And last the kerbs and barriers, which are last in the mesh too.
        Array.Copy(mesh.Indices, road + earth, indices, road + earth, mesh.TrimIndices);
        return indices;
    }

    /// <summary>How far off a mesh is: the nearest any of it can be to something <paramref name="centreM"/> from its middle.</summary>
    public static double Range(double centreM, double radiusM) => Math.Max(0.0, centreM - radiusM);

    /// <summary>The meshes that get one of <paramref name="slots"/> places: all of them, or the nearest, nearest first.</summary>
    public static List<int> Nearest(IReadOnlyList<double> rangeM, int slots)
    {
        List<int> order = [];
        for (int i = 0; i < rangeM.Count; i++) order.Add(i);
        if (order.Count <= slots) return order;

        order.Sort((a, b) => rangeM[a] != rangeM[b] ? rangeM[a].CompareTo(rangeM[b]) : a.CompareTo(b));
        int keep = Math.Max(slots, 0);
        order.RemoveRange(keep, order.Count - keep);
        return order;
    }

    /// <summary>
    /// The meshes to draw in a view, into <paramref name="picked"/>, and how many: those no more than
    /// <paramref name="withinM"/> off, and where they would take more than <paramref name="mostDraws"/>
    /// draws between them, the nearest that do not.
    /// </summary>
    /// <param name="draws">How many draws each mesh takes: one for each material it has triangles of, and none for a mesh that is not to be drawn.</param>
    /// <param name="left">How many were left out for want of draws.</param>
    public static int Pick(ReadOnlySpan<double> rangeM, ReadOnlySpan<int> draws, double withinM, int mostDraws, Span<int> picked, out int left)
    {
        int count = 0, total = 0;
        left = 0;
        for (int i = 0; i < rangeM.Length; i++)
        {
            if (draws[i] <= 0 || !(rangeM[i] <= withinM)) continue;
            picked[count++] = i;
            total += draws[i];
        }
        if (total <= mostDraws) return count;

        for (int i = 1; i < count; i++)
        {
            int moved = picked[i], j = i - 1;
            for (; j >= 0 && rangeM[picked[j]] > rangeM[moved]; j--) picked[j + 1] = picked[j];
            picked[j + 1] = moved;
        }

        int kept = 0, spent = 0;
        for (int i = 0; i < count; i++)
        {
            if (spent + draws[picked[i]] > mostDraws)
            {
                left++;
                continue;
            }
            spent += draws[picked[i]];
            picked[kept++] = picked[i];
        }
        return kept;
    }
}

/// <summary>
/// Which of a pool's places are out and which are free. Whoever keeps the pool can never give a place
/// up, only have it handed back, so one is out to no more than one holder at a time.
/// </summary>
internal sealed class SlotLedger
{
    private readonly List<bool> _out = [];
    private readonly Stack<int> _free = [];

    public int Size => _out.Count;

    public int Free => _free.Count;

    public int Used => Size - Free;

    /// <summary>Adds <paramref name="by"/> places, all free, the lowest of them the first to be given out.</summary>
    public void Grow(int by)
    {
        int size = Size;
        for (int i = 0; i < by; i++) _out.Add(false);
        for (int i = size + by - 1; i >= size; i--) _free.Push(i);
    }

    /// <summary>A free place, or -1 with none.</summary>
    public int Take()
    {
        if (_free.Count == 0) return -1;
        int slot = _free.Pop();
        _out[slot] = true;
        return slot;
    }

    /// <summary>Hands a place back. One that is not out is left alone: handed back twice, it would be given to two.</summary>
    public void Give(int slot)
    {
        if (slot < 0 || slot >= Size || !_out[slot]) return;
        _out[slot] = false;
        _free.Push(slot);
    }
}
