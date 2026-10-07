namespace KSACars;

/// <summary>
/// The circuits a player has, one file each, in a folder of the mod's own beside KSA's saves: a save
/// folder is wiped and rewritten on every write, and a circuit is not one save's.
/// </summary>
internal static class CircuitLibrary
{
    public static string Folder => Path.Combine(Directory.GetParent(Log.Folder)?.FullName ?? Log.Folder, "KSACars", "Circuits");

    public static List<string> Names()
    {
        if (!Directory.Exists(Folder)) return [];
        return [.. Directory.EnumerateFiles(Folder, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order()];
    }

    /// <summary>The circuit of that name, or null with the reason.</summary>
    public static Circuit? Load(string name, out string why)
    {
        string path = Path.Combine(Folder, Circuit.FileName(name));
        try
        {
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
