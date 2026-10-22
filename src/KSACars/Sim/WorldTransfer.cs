using System.IO.Compression;

namespace KSACars;

/// <summary>
/// The host's world on its way to a guest: a save and the laid course packed into one zip, cut into
/// pieces small enough to go whole in a datagram, sent at a pace the slowest line takes, and put
/// back together on the guest, which asks again for the pieces that went missing.
/// </summary>
internal static class WorldTransfer
{
    /// <summary>The most bytes of world in one piece: with its header, under the 1,280 any path carries whole.</summary>
    public const int ChunkBytes = 1100;

    /// <summary>How fast a world is sent, bytes a second: a home line's upload carries it, and a world of 150 KB packed is under a second.</summary>
    public const double BytesPerSecond = 400_000;

    /// <summary>The most pieces a world may be: 65,535 of them is 72 MB, a save a hundred times the size of any seen.</summary>
    public const int MostChunks = ushort.MaxValue;

    /// <summary>Files packed into one zip, each under its own name, compressed.</summary>
    public static byte[] Pack(IEnumerable<(string Name, byte[] Data)> files)
    {
        using MemoryStream into = new();
        using (ZipArchive zip = new(into, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] data) in files)
            {
                using Stream entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                entry.Write(data);
            }
        }
        return into.ToArray();
    }

    /// <summary>
    /// The files a zip holds. A name that would land outside the folder it is unpacked into is
    /// refused: the zip comes from another machine.
    /// </summary>
    public static List<(string Name, byte[] Data)> Unpack(byte[] packed)
    {
        List<(string, byte[])> files = [];
        using ZipArchive zip = new(new MemoryStream(packed), ZipArchiveMode.Read);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;
            if (!SafeName(entry.FullName)) throw new InvalidDataException($"a world holds '{entry.FullName}', which is not a name a save has");
            using Stream from = entry.Open();
            using MemoryStream data = new();
            from.CopyTo(data);
            files.Add((entry.FullName, data.ToArray()));
        }
        return files;
    }

    /// <summary>Whether a name is a plain relative path, with nothing in it that climbs out of the folder it is under.</summary>
    public static bool SafeName(string name) =>
        name.Length > 0 && !Path.IsPathRooted(name) && !name.Contains(':') && !name.Contains('\\')
        && name.Split('/').All(part => part.Length > 0 && part != "." && part != "..");

    /// <summary>A world being sent: which pieces are still to go, and whether the guest has them all.</summary>
    public sealed class Outgoing
    {
        private readonly byte[] _data;
        private readonly Queue<ushort> _due = new();
        private readonly HashSet<ushort> _queued = [];
        private double _allowance;

        public Outgoing(uint id, byte[] data)
        {
            (Id, _data) = (id, data);
            Count = (ushort)Math.Max(1, (data.Length + ChunkBytes - 1) / ChunkBytes);
            if ((data.Length + ChunkBytes - 1) / ChunkBytes > MostChunks) throw new InvalidDataException($"a world of {data.Length} bytes is too big to send");
            for (ushort i = 0; i < Count; i++) Queue(i);
        }

        public uint Id { get; }
        public ushort Count { get; }
        public int Bytes => _data.Length;

        /// <summary>Whether the guest has said it has every piece.</summary>
        public bool Done { get; set; }

        /// <summary>The pieces the pace allows after so many seconds since the last were asked for.</summary>
        public List<NetWire.Chunk> Next(double seconds)
        {
            List<NetWire.Chunk> chunks = [];
            if (Done) return chunks;
            _allowance = Math.Min(_allowance + (seconds * BytesPerSecond), 64.0 * ChunkBytes);
            while (_allowance >= ChunkBytes && _due.TryDequeue(out ushort i))
            {
                _queued.Remove(i);
                int from = i * ChunkBytes;
                chunks.Add(new NetWire.Chunk(Id, i, Count, _data[from..Math.Min(from + ChunkBytes, _data.Length)]));
                _allowance -= ChunkBytes;
            }
            return chunks;
        }

        /// <summary>Pieces the guest says it is without, sent again.</summary>
        public void Need(IEnumerable<ushort> missing)
        {
            foreach (ushort i in missing) if (i < Count) Queue(i);
        }

        private void Queue(ushort i)
        {
            if (_queued.Add(i)) _due.Enqueue(i);
        }
    }

    /// <summary>A world arriving: the pieces it has, and which are missing.</summary>
    public sealed class Incoming(uint id, ushort count)
    {
        private readonly byte[]?[] _pieces = new byte[]?[count];

        public uint Id { get; } = id;
        public ushort Count { get; } = count;
        public int Have { get; private set; }
        public bool Complete => Have == Count;

        /// <summary>The highest piece that has come, which every piece below has been sent before.</summary>
        public int Highest { get; private set; } = -1;

        public void Add(NetWire.Chunk chunk)
        {
            if (chunk.Transfer != Id || chunk.Count != Count || chunk.Index >= Count || _pieces[chunk.Index] is not null) return;
            if (chunk.Data.Length > ChunkBytes || (chunk.Index < Count - 1 && chunk.Data.Length != ChunkBytes)) return;
            _pieces[chunk.Index] = chunk.Data;
            Have++;
            Highest = Math.Max(Highest, chunk.Index);
        }

        /// <summary>
        /// The pieces to ask for again: those under the highest that has come, which were sent and lost,
        /// or every missing one when <paramref name="all"/>, for a sending that has gone quiet.
        /// </summary>
        public ushort[] Missing(bool all, int most = NetWire.MostNeeded)
        {
            List<ushort> missing = [];
            int below = all ? Count : Highest;
            for (int i = 0; i < below && missing.Count < most; i++) if (_pieces[i] is null) missing.Add((ushort)i);
            return [.. missing];
        }

        public byte[] Assemble()
        {
            if (!Complete) throw new InvalidOperationException("the world has not all come");
            return [.. _pieces.SelectMany(p => p!)];
        }
    }
}
