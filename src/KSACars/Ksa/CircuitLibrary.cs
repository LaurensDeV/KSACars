namespace KSACars;

/// <summary>
/// The circuits a player has, one file each, in a folder of the mod's own beside KSA's saves: a save
/// folder is wiped and rewritten on every write, and a circuit is not one save's.
/// </summary>
internal static class CircuitLibrary
{
    public static string Folder => Path.Combine(Directory.GetParent(Log.Folder)?.FullName ?? Log.Folder, "KSACars", "Circuits");

    /// <summary>The courses the mod comes with, beside its DLL: read and never written.</summary>
    public static string Shipped => Path.Combine(Path.GetDirectoryName(typeof(CircuitLibrary).Assembly.Location) ?? ".", "Circuits");

    /// <summary>Every circuit there is by name: the player's own, and the mod's where the player has none of that name.</summary>
    public static List<string> Names() => [.. In(Folder).Concat(In(Shipped)).Distinct().Order()];

    private static IEnumerable<string> In(string folder)
    {
        try
        {
            return Directory.Exists(folder)
                ? [.. Directory.EnumerateFiles(folder, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>()]
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// The lap the mod comes with of a course by a car, if it has one and it is of the course a player
    /// would be given by that name: one of their own of the name is another course, whatever it is called.
    /// </summary>
    public static Rival.Learnt? LapOf(string course, string car)
    {
        try
        {
            string lap = Path.Combine(Shipped, "Laps", course, car + ".json");
            if (!File.Exists(lap)) return null;
            if (Rival.Read(File.ReadAllText(lap), out string why) is not { } learnt)
            {
                Log.Warn($"the lap of '{course}' by {car} could not be read: {why}");
                return null;
            }

            string path = Path.Combine(Folder, Circuit.FileName(course));
            if (!File.Exists(path)) path = Path.Combine(Shipped, Circuit.FileName(course));
            if (!File.Exists(path)) return null;
            string sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
            return string.Equals(sha, learnt.CircuitSha256, StringComparison.OrdinalIgnoreCase) ? learnt : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The circuit of that name, or null with the reason.</summary>
    public static Circuit? Load(string name, out string why)
    {
        string path = Path.Combine(Folder, Circuit.FileName(name));
        try
        {
            // The player's own of that name before the mod's.
            if (!File.Exists(path) && Path.Combine(Shipped, Circuit.FileName(name)) is var shipped && File.Exists(shipped)) path = shipped;
            if (!File.Exists(path))
            {
                why = $"no circuit '{name}' in {Folder}";
                return null;
            }
            Circuit? circuit = Circuit.FromJson(File.ReadAllText(path), out why, out int dropped);
            if (dropped > 0) Log.Warn($"circuit '{name}': {dropped} road(s) left out as broken");
            return circuit;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            why = e.Message;
            return null;
        }
    }

    /// <summary>Written aside and moved over the old file, so a crash mid-write leaves the old circuit.</summary>
    public static bool Save(Circuit circuit, out string why)
    {
        why = "";
        try
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, Circuit.FileName(circuit.Name));
            File.WriteAllText(path + ".part", circuit.ToJson());
            File.Move(path + ".part", path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            why = e.Message;
            return false;
        }
    }
}
