using System.Text;
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
///
/// <para>In its file a circuit is one place on a body and every point as metres east and north of
/// it, so the same file can be laid anywhere: <see cref="MovedTo"/> is that, and the file is
/// <see cref="ToJson"/>'s. A file of an earlier version, which has a latitude and a longitude a
/// point, is read with what it does not say at its default: level across, one width a road, the
/// ground smoothed over 30 m. A point below the ground in one is kept as written and laid on the
/// ground, since a road can be filled under and never cut in.</para>
/// </summary>
internal sealed record Circuit
{
    public const int CurrentVersion = 3;

    /// <summary>The body's radius a file's metres are taken on where nothing says: Earth's, and a file read with what it was written with comes back the same whatever that is.</summary>
    public const double DefaultRadiusM = 6_371_000.0;
    public const double MinWidthM = 2.0, MaxWidthM = 40.0;

    /// <summary>A handle no longer than half the road it is on, at each end, so the two cannot cross.</summary>
    public const double MaxCorner = 1.5;

    /// <summary>What a file may say. A point is set no lower than the ground: see <see cref="SetHeight"/>.</summary>
    public const double MinHeightM = -20.0, MaxHeightM = 10_000.0;

    public const double MaxBankDeg = 30.0;

    public const double DefaultGroundSmoothM = 30.0, MaxGroundSmoothM = 200.0;

    public const double MaxJunctionRadiusM = 100.0;

    public int Version { get; init; } = CurrentVersion;
    public string Name { get; init; } = "";
    public string Body { get; init; } = "";
    public double WidthM { get; init; } = 10.0;

    /// <summary>How far along a road the ground under it is smoothed over (m): what a bump shorter than this is filled across.</summary>
    public double GroundSmoothM { get; init; } = DefaultGroundSmoothM;
    public IReadOnlyList<Node> Nodes { get; init; } = [];
    public IReadOnlyList<Road> Roads { get; init; } = [];

    /// <summary>The mean radius of the body the points are on, m, which is what makes a file's metres metres; nothing where it is not known.</summary>
    [JsonIgnore]
    public double RadiusM { get; init; }

    /// <summary>The points a lap goes through, in order, and the stretches of it, in metres along, where a crest is meant to be jumped: kept for whoever drives it.</summary>
    public IReadOnlyList<int>? Route { get; init; }
    public IReadOnlyList<double[]>? Jumps { get; init; }

    /// <summary>
    /// A point a road goes through. <paramref name="Corner"/> is how far the road reaches before it
    /// turns there: 0 is a kink, 1 a curve that leaves along a third of each road.
    /// <paramref name="HeightM"/> is how far above the ground the road is there.
    /// <paramref name="JunctionRadiusM"/> is how far the corners between the roads meeting there are rounded.
    /// </summary>
    public sealed record Node(int Id, double LatDeg, double LonDeg, double Corner = 1.0, double HeightM = 0.0, double? JunctionRadiusM = null);

    public sealed record Place(double LatDeg, double LonDeg);

    /// <summary>
    /// A road between two points. A handle is where the road is pulled towards as it leaves that end,
    /// set by hand; without one it is worked out from the roads that meet there. A bank is how far
    /// the road leans at that end, in degrees, raising the left edge of one travelling from
    /// <paramref name="From"/> to <paramref name="To"/>; a width at an end is the road's there, and it
    /// eases from one to the other.
    /// </summary>
    public sealed record Road(int From, int To, double? WidthM = null, Place? FromHandle = null, Place? ToHandle = null,
                              double? FromBankDeg = null, double? ToBankDeg = null, double? FromWidthM = null, double? ToWidthM = null)
    {
        public bool Joins(int a, int b) => (From == a && To == b) || (From == b && To == a);

        public bool Touches(int node) => From == node || To == node;
    }

    public Node? Find(int id) => Nodes.FirstOrDefault(n => n.Id == id);

    public double WidthOf(Road road) => Math.Clamp(road.WidthM ?? WidthM, MinWidthM, MaxWidthM);

    /// <summary>The width set for a road's end at <paramref name="node"/>, or none where it is the road's own.</summary>
    public static double? EndWidth(Road road, int node) =>
        (road.From == node ? road.FromWidthM : road.ToWidthM) is { } w ? Math.Clamp(w, MinWidthM, MaxWidthM) : null;

    /// <summary>The bank set for a road's end at <paramref name="node"/>, as the road is saved, or none where it is level.</summary>
    public static double? EndBankDeg(Road road, int node) =>
        (road.From == node ? road.FromBankDeg : road.ToBankDeg) is { } b ? Math.Clamp(b, -MaxBankDeg, MaxBankDeg) : null;

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

    /// <summary>
    /// No lower than the ground. KSA's terrain cannot be cut into and a wheel rides whichever of the
    /// road and the ground is higher, so a road set below it would be one nothing drives on.
    /// </summary>
    public Circuit SetHeight(int id, double heightM) => this with
    {
        Nodes = [.. Nodes.Select(n => n.Id == id ? n with { HeightM = Math.Clamp(heightM, 0.0, MaxHeightM) } : n)],
    };

    public Circuit SetJunctionRadius(int id, double? radiusM) => this with
    {
        Nodes = [.. Nodes.Select(n => n.Id == id ? n with { JunctionRadiusM = radiusM is { } r ? Math.Clamp(r, 0.0, MaxJunctionRadiusM) : null } : n)],
    };

    public Circuit SetGroundSmooth(double metres) => this with { GroundSmoothM = Math.Clamp(metres, 0.0, MaxGroundSmoothM) };

    /// <summary>The bank of the road between two points at its <paramref name="at"/> end, or none for level.</summary>
    public Circuit SetBank(int at, int other, double? bankDeg)
    {
        double? bank = bankDeg is { } b ? Math.Clamp(b, -MaxBankDeg, MaxBankDeg) : null;
        return this with
        {
            Roads = [.. Roads.Select(r => !r.Joins(at, other) ? r : r.From == at ? r with { FromBankDeg = bank } : r with { ToBankDeg = bank })],
        };
    }

    /// <summary>The width of the road between two points at its <paramref name="at"/> end, or none for the road's own.</summary>
    public Circuit SetEndWidth(int at, int other, double? widthM)
    {
        double? width = widthM is { } w ? Math.Clamp(w, MinWidthM, MaxWidthM) : null;
        return this with
        {
            Roads = [.. Roads.Select(r => !r.Joins(at, other) ? r : r.From == at ? r with { FromWidthM = width } : r with { ToWidthM = width })],
        };
    }

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

    /// <summary>A point put into a road, which becomes two; the far ends keep their handles, their banks and their widths.</summary>
    public Circuit Split(int a, int b, double latDeg, double lonDeg, out int id)
    {
        id = 0;
        if (Roads.FirstOrDefault(r => r.Joins(a, b)) is not { } road) return this;
        Circuit with_ = AddNode(latDeg, lonDeg, out id);
        int mid = id;
        return with_ with
        {
            Roads = [.. Roads.Where(r => !ReferenceEquals(r, road)),
                     road with { To = mid, ToHandle = null, ToBankDeg = null, ToWidthM = null },
                     road with { From = mid, FromHandle = null, FromBankDeg = null, FromWidthM = null }],
        };
    }

    // ---- where it is -------------------------------------------------------------------------

    /// <summary>
    /// The circuit as it would be with its first point at another place, on this body or another, and
    /// turned about that point by <paramref name="headingDeg"/> clockwise seen from above: every point
    /// and handle as far east and north of the first, in metres, as it was.
    /// </summary>
    /// <param name="radiusM">The mean radius of the body it is moved to.</param>
    public Circuit MovedTo(double latDeg, double lonDeg, double headingDeg, double radiusM, string? body = null)
    {
        if (Nodes.Count == 0 || !(radiusM > 0.0)) return this with { RadiusM = radiusM, Body = body ?? Body };

        RoadChart from = Chart(Nodes[0].LatDeg, Nodes[0].LonDeg, RadiusM > 0.0 ? RadiusM : radiusM), to = Chart(latDeg, lonDeg, radiusM);
        double turn = headingDeg * Math.PI / 180.0, cos = Math.Cos(turn), sin = Math.Sin(turn);
        (double Lat, double Lon) Moved(double lat, double lon)
        {
            Plan at = from.Of(DirOf(lat, lon));
            return LatLonOf(to.Dir(new Plan((at.E * cos) + (at.N * sin), (at.N * cos) - (at.E * sin))));
        }
        Place? Handle(Place? p) => p is null ? null : Moved(p.LatDeg, p.LonDeg) is var (lat, lon) ? new Place(lat, lon) : null;
        return this with
        {
            Body = body ?? Body,
            RadiusM = radiusM,
            Nodes = [.. Nodes.Select(n => Moved(n.LatDeg, n.LonDeg) is var (lat, lon) ? n with { LatDeg = lat, LonDeg = lon } : n)],
            Roads = [.. Roads.Select(r => r with { FromHandle = Handle(r.FromHandle), ToHandle = Handle(r.ToHandle) })],
        };
    }

    /// <summary>The way the first road leaves the first point, in degrees clockwise from north; nothing with no road.</summary>
    public double StartBearingDeg()
    {
        if (Nodes.Count == 0 || Roads.FirstOrDefault(r => r.From == Nodes[0].Id) is not { } road || Find(road.To) is not { } to) return 0.0;
        Plan step = Chart(Nodes[0].LatDeg, Nodes[0].LonDeg, DefaultRadiusM).Of(DirOf(to.LatDeg, to.LonDeg));
        return Math.Atan2(step.E, step.N) * 180.0 / Math.PI;
    }

    // A chart about a place, east and north as the compass has them. The axes are this file's own: only angles between places leave it.
    private static RoadChart Chart(double latDeg, double lonDeg, double radiusM) => new(DirOf(latDeg, lonDeg), radiusM, new Brutal.Numerics.double3(0.0, 0.0, 1.0));

    private static Brutal.Numerics.double3 DirOf(double latDeg, double lonDeg)
    {
        double lat = latDeg * Math.PI / 180.0, lon = lonDeg * Math.PI / 180.0;
        return new Brutal.Numerics.double3(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
    }

    private static (double Lat, double Lon) LatLonOf(Brutal.Numerics.double3 dir)
    {
        Brutal.Numerics.double3 d = Vec.Unit(dir);
        return (Math.Asin(Math.Clamp(d.Z, -1.0, 1.0)) * 180.0 / Math.PI, Math.Atan2(d.Y, d.X) * 180.0 / Math.PI);
    }

    // ---- the file ---------------------------------------------------------------------------

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions Line = new(Options) { WriteIndented = false };

    // The file: one place, and every point and handle as metres from it. A millimetre is kept of each.
    private sealed record Saved(int Version, string? Name, string? Body, double? RadiusM, SavedAt? At, double? WidthM, double? GroundSmoothM,
                                List<SavedNode>? Nodes, List<SavedRoad>? Roads, IReadOnlyList<int>? Route, IReadOnlyList<double[]>? Jumps);

    private sealed record SavedAt(double LatDeg, double LonDeg, double? HeadingDeg);

    private sealed record SavedNode(int Id, double EastM, double NorthM, double? HeightM, double? Corner, double? JunctionRadiusM);

    private sealed record SavedPlace(double EastM, double NorthM);

    private sealed record SavedRoad(int From, int To, double? WidthM, SavedPlace? FromHandle, SavedPlace? ToHandle,
                                    double? FromBankDeg, double? ToBankDeg, double? FromWidthM, double? ToWidthM);

    /// <summary>
    /// The circuit as its file: the place of its first point, and each point a line, as metres east
    /// and north of that place to the millimetre, with what is at its default left out.
    /// </summary>
    public string ToJson()
    {
        double radius = RadiusM > 0.0 ? RadiusM : DefaultRadiusM;
        (double lat, double lon) = Nodes.Count > 0 ? (Math.Round(Nodes[0].LatDeg, 9), Math.Round(Nodes[0].LonDeg, 9)) : (0.0, 0.0);
        RoadChart chart = Chart(lat, lon, radius);
        SavedPlace At(double la, double lo) => chart.Of(DirOf(la, lo)) is var p ? new SavedPlace(Mm(p.E), Mm(p.N)) : null!;
        static double Mm(double v) => Math.Round(v, 3) + 0.0;
        static double? Set(double? v) => v is { } x ? Mm(x) : null;
        static string One<T>(T value) => JsonSerializer.Serialize(value, Line);

        StringBuilder text = new();
        text.Append("{\n");
        text.Append("  \"version\": ").Append(CurrentVersion).Append(",\n");
        text.Append("  \"name\": ").Append(One(Name)).Append(",\n");
        text.Append("  \"body\": ").Append(One(Body)).Append(",\n");
        text.Append("  \"radius_m\": ").Append(One(Mm(radius))).Append(",\n");
        text.Append("  \"at\": ").Append(One(new SavedAt(lat, lon, null))).Append(",\n");
        text.Append("  \"width_m\": ").Append(One(Mm(WidthM))).Append(",\n");
        if (Mm(GroundSmoothM) != DefaultGroundSmoothM) text.Append("  \"ground_smooth_m\": ").Append(One(Mm(GroundSmoothM))).Append(",\n");
        if (Route is { Count: > 0 }) text.Append("  \"route\": ").Append(One(Route)).Append(",\n");
        if (Jumps is { Count: > 0 }) text.Append("  \"jumps\": ").Append(One(Jumps)).Append(",\n");

        void Lines<T>(string name, IEnumerable<T> items, bool last)
        {
            text.Append("  \"").Append(name).Append("\": [");
            string between = "\n    ";
            foreach (T item in items)
            {
                text.Append(between).Append(One(item));
                between = ",\n    ";
            }
            text.Append(between == "\n    " ? "]" : "\n  ]").Append(last ? "\n" : ",\n");
        }
        Lines("nodes", Nodes.Select(n => At(n.LatDeg, n.LonDeg) is var p
            ? new SavedNode(n.Id, p.EastM, p.NorthM, Mm(n.HeightM) == 0.0 ? null : Mm(n.HeightM), Mm(n.Corner) == 1.0 ? null : Mm(n.Corner), Set(n.JunctionRadiusM))
            : null!), last: false);
        Lines("roads", Roads.Select(r => new SavedRoad(r.From, r.To, Set(r.WidthM),
            r.FromHandle is { } f ? At(f.LatDeg, f.LonDeg) : null, r.ToHandle is { } t ? At(t.LatDeg, t.LonDeg) : null,
            Set(r.FromBankDeg), Set(r.ToBankDeg), Set(r.FromWidthM), Set(r.ToWidthM))), last: true);
        text.Append("}\n");
        return text.ToString();
    }

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
            using JsonDocument file = JsonDocument.Parse(json);
            int version = file.RootElement.ValueKind == JsonValueKind.Object && file.RootElement.TryGetProperty("version", out JsonElement v)
                          && v.TryGetInt32(out int said) ? said : 1;
            if (version > CurrentVersion)
            {
                why = $"written by a newer build (version {version})";
                return null;
            }
            read = version >= 3 ? Placed(JsonSerializer.Deserialize<Saved>(json, Options)) : JsonSerializer.Deserialize<Circuit>(json, Options);
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

        List<Node> nodes = [];
        foreach (Node n in read.Nodes)
        {
            if (n is null || nodes.Any(k => k.Id == n.Id) || !double.IsFinite(n.LatDeg) || !double.IsFinite(n.LonDeg)) continue;
            nodes.Add(n with
            {
                Corner = double.IsFinite(n.Corner) ? Math.Clamp(n.Corner, 0.0, MaxCorner) : 1.0,
                HeightM = double.IsFinite(n.HeightM) ? Math.Clamp(n.HeightM, MinHeightM, MaxHeightM) : 0.0,
                JunctionRadiusM = Kept(n.JunctionRadiusM, 0.0, MaxJunctionRadiusM),
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
            roads.Add(r with
            {
                FromBankDeg = Kept(r.FromBankDeg, -MaxBankDeg, MaxBankDeg),
                ToBankDeg = Kept(r.ToBankDeg, -MaxBankDeg, MaxBankDeg),
                FromWidthM = Kept(r.FromWidthM, MinWidthM, MaxWidthM),
                ToWidthM = Kept(r.ToWidthM, MinWidthM, MaxWidthM),
            });
        }
        return read with
        {
            Version = CurrentVersion,
            GroundSmoothM = double.IsFinite(read.GroundSmoothM) ? Math.Clamp(read.GroundSmoothM, 0.0, MaxGroundSmoothM) : DefaultGroundSmoothM,
            Nodes = nodes,
            Roads = roads,
            WidthM = double.IsFinite(read.WidthM) ? Math.Clamp(read.WidthM, MinWidthM, MaxWidthM) : 10.0,
        };
    }

    // A file's metres as places on the body, about the place it gives and turned as it says.
    private static Circuit? Placed(Saved? file)
    {
        if (file is null || file.Nodes is null || file.Roads is null) return null;
        double radius = file.RadiusM is { } r && double.IsFinite(r) && r > 0.0 ? r : DefaultRadiusM;
        SavedAt at = file.At is { } a && double.IsFinite(a.LatDeg) && double.IsFinite(a.LonDeg) ? a : new SavedAt(0.0, 0.0, null);
        RoadChart chart = Chart(at.LatDeg, at.LonDeg, radius);
        double turn = (at.HeadingDeg is { } h && double.IsFinite(h) ? h : 0.0) * Math.PI / 180.0, cos = Math.Cos(turn), sin = Math.Sin(turn);
        (double Lat, double Lon) On(double east, double north) => LatLonOf(chart.Dir(new Plan((east * cos) + (north * sin), (north * cos) - (east * sin))));
        Place? Handle(SavedPlace? p) => p is null ? null : On(p.EastM, p.NorthM) is var (lat, lon) ? new Place(lat, lon) : null;

        return new Circuit
        {
            Version = file.Version, Name = file.Name ?? "", Body = file.Body ?? "", RadiusM = radius, WidthM = file.WidthM ?? 10.0,
            GroundSmoothM = file.GroundSmoothM ?? DefaultGroundSmoothM, Route = file.Route, Jumps = file.Jumps,
            Nodes = [.. file.Nodes.Where(n => n is not null).Select(n => On(n.EastM, n.NorthM) is var (lat, lon)
                ? new Node(n.Id, lat, lon, n.Corner ?? 1.0, n.HeightM ?? 0.0, n.JunctionRadiusM) : null!)],
            Roads = [.. file.Roads.Where(x => x is not null).Select(x => new Road(x.From, x.To, x.WidthM, Handle(x.FromHandle), Handle(x.ToHandle),
                x.FromBankDeg, x.ToBankDeg, x.FromWidthM, x.ToWidthM))],
        };
    }

    private static double? Kept(double? value, double least, double most) =>
        value is { } v && double.IsFinite(v) ? Math.Clamp(v, least, most) : null;

    /// <summary>A circuit's name as a file's: letters, digits, spaces, dashes and underscores, and never empty.</summary>
    public static string FileName(string name)
    {
        string kept = new([.. name.Trim().Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' ? c : '_')]);
        return (kept.Length == 0 ? "circuit" : kept.Length > 60 ? kept[..60] : kept) + ".json";
    }
}
