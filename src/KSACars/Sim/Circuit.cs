using System.Text.Json;
using System.Text.Json.Serialization;

namespace KSACars;

/// <summary>
/// A set of roads as it is saved: points on a body, and which of them a road joins.
///
/// <para>A point is a latitude and a longitude and nothing else, so the road follows whatever ground
/// is there, at the height above it the point gives, and a KSA update that reshapes the terrain moves it with the ground. Three or more
/// roads at one point are a junction. A circuit is never changed in place: every edit answers a new
/// one, so the one before is the undo.</para>
/// </summary>
internal sealed record Circuit
{
    public const int CurrentVersion = 1;
    public const double MinWidthM = 2.0, MaxWidthM = 40.0;

    /// <summary>A handle no longer than half the road it is on, at each end, so the two cannot cross.</summary>
    public const double MaxCorner = 1.5;

    public const double MinHeightM = -20.0, MaxHeightM = 10_000.0;

    public int Version { get; init; } = CurrentVersion;
    public string Name { get; init; } = "";
    public string Body { get; init; } = "";
    public double WidthM { get; init; } = 10.0;
    public IReadOnlyList<Node> Nodes { get; init; } = [];
    public IReadOnlyList<Road> Roads { get; init; } = [];

    /// <summary>
    /// A point a road goes through. <paramref name="Corner"/> is how far the road reaches before it
    /// turns there: 0 is a kink, 1 a curve that leaves along a third of each road.
    /// <paramref name="HeightM"/> is how far above the ground the road is there, or below it.
    /// </summary>
    public sealed record Node(int Id, double LatDeg, double LonDeg, double Corner = 1.0, double HeightM = 0.0);

    public sealed record Place(double LatDeg, double LonDeg);

    /// <summary>
    /// A road between two points. A handle is where the road is pulled towards as it leaves that end,
    /// set by hand; without one it is worked out from the roads that meet there.
    /// </summary>
    public sealed record Road(int From, int To, double? WidthM = null, Place? FromHandle = null, Place? ToHandle = null)
    {
        public bool Joins(int a, int b) => (From == a && To == b) || (From == b && To == a);

        public bool Touches(int node) => From == node || To == node;
    }

    public Node? Find(int id) => Nodes.FirstOrDefault(n => n.Id == id);

    public double WidthOf(Road road) => Math.Clamp(road.WidthM ?? WidthM, MinWidthM, MaxWidthM);

    // ---- edits ------------------------------------------------------------------------------

    public Circuit AddNode(double latDeg, double lonDeg, out int id)
    {
        id = Nodes.Count == 0 ? 1 : Nodes.Max(n => n.Id) + 1;
        return this with { Nodes = [.. Nodes, new Node(id, latDeg, lonDeg)] };
    }

    /// <summary>A new point joined to <paramref name="from"/> by a road: how a road is drawn, a click at a time.</summary>
    public Circuit Extend(int from, double latDeg, double lonDeg, out int id) => AddNode(latDeg, lonDeg, out id).Connect(from, id);

    public Circuit Connect(int a, int b)
    {
        if (a == b || Find(a) is null || Find(b) is null || Roads.Any(r => r.Joins(a, b))) return this;
        return this with { Roads = [.. Roads, new Road(a, b)] };
    }

    /// <summary>The point moved, and the handles at it with it, so the shape of its roads goes along.</summary>
    public Circuit MoveNode(int id, double latDeg, double lonDeg)
    {
        if (Find(id) is not { } node) return this;
        double dLat = latDeg - node.LatDeg, dLon = lonDeg - node.LonDeg;
        Place? Moved(Place? p) => p is null ? null : new Place(p.LatDeg + dLat, p.LonDeg + dLon);
        return this with
        {
            Nodes = [.. Nodes.Select(n => n.Id == id ? n with { LatDeg = latDeg, LonDeg = lonDeg } : n)],
            Roads = [.. Roads.Select(r => r with
            {
                FromHandle = r.From == id ? Moved(r.FromHandle) : r.FromHandle,
                ToHandle = r.To == id ? Moved(r.ToHandle) : r.ToHandle,
            })],
        };
    }

    public Circuit RemoveNode(int id) => this with
    {
        Nodes = [.. Nodes.Where(n => n.Id != id)],
        Roads = [.. Roads.Where(r => !r.Touches(id))],
    };

    public Circuit RemoveRoad(int a, int b) => this with { Roads = [.. Roads.Where(r => !r.Joins(a, b))] };

    public Circuit SetCorner(int id, double corner) => this with
    {
        Nodes = [.. Nodes.Select(n => n.Id == id ? n with { Corner = Math.Clamp(corner, 0.0, MaxCorner) } : n)],
    };

    public Circuit SetHeight(int id, double heightM) => this with
    {
        Nodes = [.. Nodes.Select(n => n.Id == id ? n with { HeightM = Math.Clamp(heightM, MinHeightM, MaxHeightM) } : n)],
    };

    public Circuit SetWidth(int a, int b, double? widthM) => this with
    {
        Roads = [.. Roads.Select(r => r.Joins(a, b) ? r with { WidthM = widthM is { } w ? Math.Clamp(w, MinWidthM, MaxWidthM) : null } : r)],
    };

    /// <summary>The handle of the road between two points at its <paramref name="at"/> end, or none to have it worked out again.</summary>
    public Circuit SetHandle(int at, int other, Place? handle) => this with
    {
        Roads = [.. Roads.Select(r => !r.Joins(at, other) ? r
            : r.From == at ? r with { FromHandle = handle } : r with { ToHandle = handle })],
    };

    /// <summary>A point put into a road, which becomes two; the far ends keep their handles.</summary>
    public Circuit Split(int a, int b, double latDeg, double lonDeg, out int id)
    {
        id = 0;
        if (Roads.FirstOrDefault(r => r.Joins(a, b)) is not { } road) return this;
        Circuit with_ = AddNode(latDeg, lonDeg, out id);
        int mid = id;
        return with_ with
        {
            Roads = [.. Roads.Where(r => !ReferenceEquals(r, road)),
                     new Road(road.From, mid, road.WidthM, road.FromHandle),
                     new Road(mid, road.To, road.WidthM, null, road.ToHandle)],
        };
    }

    // ---- the file ---------------------------------------------------------------------------

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>
    /// The circuit read, or null with the reason. One from a newer build is refused rather than half
    /// read; a road to a point that is not there, to itself, or laid twice is left out and counted in
    /// <paramref name="dropped"/>.
    /// </summary>
    public static Circuit? FromJson(string json, out string why, out int dropped)
    {
        why = "";
        dropped = 0;
        Circuit? read;
        try
        {
            read = JsonSerializer.Deserialize<Circuit>(json, Options);
        }
        catch (JsonException e)
        {
            why = e.Message;
            return null;
        }
        if (read is null || read.Nodes is null || read.Roads is null)
        {
            why = "empty";
            return null;
        }
        if (read.Version > CurrentVersion)
        {
            why = $"written by a newer build (version {read.Version})";
            return null;
        }

        List<Node> nodes = [];
        foreach (Node n in read.Nodes)
        {
            if (n is null || nodes.Any(k => k.Id == n.Id) || !double.IsFinite(n.LatDeg) || !double.IsFinite(n.LonDeg)) continue;
            nodes.Add(n with
            {
                Corner = double.IsFinite(n.Corner) ? Math.Clamp(n.Corner, 0.0, MaxCorner) : 1.0,
                HeightM = double.IsFinite(n.HeightM) ? Math.Clamp(n.HeightM, MinHeightM, MaxHeightM) : 0.0,
            });
        }
        List<Road> roads = [];
        foreach (Road r in read.Roads)
        {
            if (r is null || r.From == r.To || !nodes.Any(n => n.Id == r.From) || !nodes.Any(n => n.Id == r.To)
                || roads.Any(k => k.Joins(r.From, r.To)))
            {
                dropped++;
                continue;
            }
            roads.Add(r);
        }
        return read with
        {
            Nodes = nodes,
            Roads = roads,
            WidthM = double.IsFinite(read.WidthM) ? Math.Clamp(read.WidthM, MinWidthM, MaxWidthM) : 10.0,
        };
    }

    /// <summary>A circuit's name as a file's: letters, digits, spaces, dashes and underscores, and never empty.</summary>
    public static string FileName(string name)
    {
        string kept = new([.. name.Trim().Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' ? c : '_')]);
        return (kept.Length == 0 ? "circuit" : kept.Length > 60 ? kept[..60] : kept) + ".json";
    }
}
