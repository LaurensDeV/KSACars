using System.Buffers.Binary;
using System.Text;
using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// What two games racing each other say, as UDP datagrams: a four-byte mark and a protocol version,
/// so a datagram from anything else is dropped, a kind, and the body. Little-endian throughout.
///
/// <para>Nothing is resent: a state is stale by the time a resend would arrive, so the next one is
/// what is wanted, and what has to arrive (a hello, the roster, a start) is sent again until it is
/// answered or superseded.</para>
/// </summary>
internal static class NetWire
{
    /// <summary>Changed whenever what a datagram means changes: a game speaking another version is refused by name.</summary>
    public const ushort Version = 2;

    /// <summary>
    /// Room enough for any datagram: a roster of sixteen racers named in emoji. A state is 102 bytes,
    /// and every datagram a race sends in plain names is under the 1,280 any path carries whole.
    /// </summary>
    public const int BufferBytes = 8192;

    public const int NameChars = 24;

    private static ReadOnlySpan<byte> Mark => "FPR!"u8;

    public enum Kind : byte
    {
        /// <summary>A client asking in: its name, its car, and the course it has laid.</summary>
        Hello = 1,

        /// <summary>The host's answer to a hello: the number the client is known by, or why it is refused.</summary>
        Welcome = 2,

        /// <summary>Everyone in the race, from the host, whenever that changes and every so often.</summary>
        Roster = 3,

        /// <summary>A car where it is, from its driver to the host and from the host to everyone else.</summary>
        State = 4,

        Ping = 5,
        Pong = 6,

        /// <summary>A race starting at a time on the host's clock.</summary>
        Start = 7,

        Bye = 8,

        /// <summary>Nothing: sent only to open the way through a router to whoever it is sent to.</summary>
        Punch = 9,

        /// <summary>The host's universe time and how fast it runs, which every guest's world keeps to.</summary>
        Clock = 10,

        /// <summary>A piece of the host's world, on its way to a guest.</summary>
        Chunk = 11,

        /// <summary>A guest's list of the pieces of a world it is still without.</summary>
        Need = 12,

        /// <summary>A guest has every piece of a world.</summary>
        Got = 13,

        /// <summary>A guest has loaded the world and laid its course, and is ready to race.</summary>
        Loaded = 14,
    }

    /// <param name="Mods">The ids of every mod the guest's game has enabled, comma separated: a world holding a part of one it lacks cannot be loaded.</param>
    public sealed record Hello(string Name, string Car, string KsaBuild, string Mods) : Message(Kind.Hello);

    /// <param name="Id">The number the client is known by from now, or zero with <paramref name="Refused"/> saying why.</param>
    public sealed record Welcome(byte Id, string Refused) : Message(Kind.Welcome);

    /// <param name="Craft">The Id of the racer's car in every copy of the world, or nothing before it has one.</param>
    public readonly record struct Racer(byte Id, string Name, string Car, byte Box, string Craft);

    public sealed record Roster(string Course, string CourseHash, Racer[] Racers) : Message(Kind.Roster);

    /// <param name="Sent">On the sender's own clock, s.</param>
    /// <param name="Steer">-1 to 1, a left turn positive, as the drive takes it.</param>
    public sealed record State(byte Id, uint Sequence, double Sent, RacePose Pose, float Throttle, float Steer, float Brake,
                               byte Gear, bool Lights) : Message(Kind.State);

    public sealed record Ping(double Sent) : Message(Kind.Ping);

    /// <param name="Echo">The ping's own time, on the asker's clock.</param>
    /// <param name="Answered">When it was answered, on the answerer's clock.</param>
    public sealed record Pong(double Echo, double Answered) : Message(Kind.Pong);

    /// <param name="At">When the lights go out, on the host's clock.</param>
    public sealed record Start(uint Race, double At, byte Laps) : Message(Kind.Start);

    public sealed record Bye() : Message(Kind.Bye);

    /// <param name="Universe">The host's universe time, s.</param>
    /// <param name="Sent">When it was that time, on the host's own clock, s.</param>
    /// <param name="Speed">How fast the host's world runs: 1, or 0 while it is held for a guest to join.</param>
    public sealed record Clock(double Universe, double Sent, float Speed) : Message(Kind.Clock);

    public sealed record Chunk(uint Transfer, ushort Index, ushort Count, byte[] Data) : Message(Kind.Chunk);

    public sealed record Need(uint Transfer, ushort[] Missing) : Message(Kind.Need);

    public sealed record Got(uint Transfer) : Message(Kind.Got);

    public sealed record Loaded(uint Transfer) : Message(Kind.Loaded);

    public sealed record Punch() : Message(Kind.Punch);

    public abstract record Message(Kind Kind);

    public static int Write(Message message, Span<byte> into)
    {
        Writer w = new(into);
        w.Bytes(Mark);
        w.U16(Version);
        w.U8((byte)message.Kind);
        switch (message)
        {
            case Hello h:
                w.Text(h.Name, NameChars);
                w.Text(h.Car, 64);
                w.Text(h.KsaBuild, 64);
                w.LongText(h.Mods, 4000);
                break;
            case Welcome m:
                w.U8(m.Id);
                w.Text(m.Refused, 200);
                break;
            case Roster r:
                w.Text(r.Course, 64);
                w.Text(r.CourseHash, 64);
                w.U8((byte)Math.Min(r.Racers.Length, 16));
                foreach (Racer racer in r.Racers.Take(16))
                {
                    w.U8(racer.Id);
                    w.Text(racer.Name, NameChars);
                    w.Text(racer.Car, 64);
                    w.U8(racer.Box);
                    w.Text(racer.Craft, 64);
                }
                break;
            case State s:
                w.U8(s.Id);
                w.U32(s.Sequence);
                w.F64(s.Sent);
                w.F64(s.Pose.S);
                w.F64(s.Pose.LengthM);
                w.V3(s.Pose.Offset);
                w.F32((float)s.Pose.Attitude.X);
                w.F32((float)s.Pose.Attitude.Y);
                w.F32((float)s.Pose.Attitude.Z);
                w.F32((float)s.Pose.Attitude.W);
                w.V3(s.Pose.Velocity);
                w.V3(s.Pose.SpinBody);
                w.F32(s.Throttle);
                w.F32(s.Steer);
                w.F32(s.Brake);
                w.U8(s.Gear);
                w.U8(s.Lights ? (byte)1 : (byte)0);
                break;
            case Ping p:
                w.F64(p.Sent);
                break;
            case Pong p:
                w.F64(p.Echo);
                w.F64(p.Answered);
                break;
            case Start s:
                w.U32(s.Race);
                w.F64(s.At);
                w.U8(s.Laps);
                break;
            case Clock c:
                w.F64(c.Universe);
                w.F64(c.Sent);
                w.F32(c.Speed);
                break;
            case Chunk c:
                w.U32(c.Transfer);
                w.U16(c.Index);
                w.U16(c.Count);
                w.U16((ushort)c.Data.Length);
                w.Bytes(c.Data);
                break;
            case Need n:
                w.U32(n.Transfer);
                w.U16((ushort)Math.Min(n.Missing.Length, MostNeeded));
                foreach (ushort i in n.Missing.Take(MostNeeded)) w.U16(i);
                break;
            case Got g:
                w.U32(g.Transfer);
                break;
            case Loaded l:
                w.U32(l.Transfer);
                break;
        }
        return w.At;
    }

    /// <summary>
    /// A datagram read, or null for one that is not ours, is of another version, or is cut short:
    /// <paramref name="version"/> says which version it was, for a refusal to name.
    /// </summary>
    public static Message? Read(ReadOnlySpan<byte> data, out ushort version)
    {
        version = 0;
        if (data.Length < 7 || !data[..4].SequenceEqual(Mark)) return null;
        version = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        if (version != Version) return null;
        Reader r = new(data[7..]);
        try
        {
            Message m = (Kind)data[6] switch
            {
                Kind.Hello => new Hello(r.Text(NameChars), r.Text(64), r.Text(64), r.LongText(4000)),
                Kind.Welcome => new Welcome(r.U8(), r.Text(200)),
                Kind.Roster => ReadRoster(ref r),
                Kind.State => new State(r.U8(), r.U32(), r.F64(),
                                        new RacePose(r.F64(), r.F64(), r.V3(), doubleQuat.Normalize(new doubleQuat(r.F32(), r.F32(), r.F32(), r.F32())), r.V3(), r.V3()),
                                        r.F32(), r.F32(), r.F32(), r.U8(), r.U8() != 0),
                Kind.Ping => new Ping(r.F64()),
                Kind.Pong => new Pong(r.F64(), r.F64()),
                Kind.Start => new Start(r.U32(), r.F64(), r.U8()),
                Kind.Bye => new Bye(),
                Kind.Punch => new Punch(),
                Kind.Clock => new Clock(r.F64(), r.F64(), r.F32()),
                Kind.Chunk => new Chunk(r.U32(), r.U16(), r.U16(), r.Take(r.U16()).ToArray()),
                Kind.Need => ReadNeed(ref r),
                Kind.Got => new Got(r.U32()),
                Kind.Loaded => new Loaded(r.U32()),
                _ => throw new FormatException(),
            };
            return m is State s && !Finite(s) ? null : m;
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or FormatException or DecoderFallbackException)
        {
            return null;
        }
    }

    private static Roster ReadRoster(ref Reader r)
    {
        string course = r.Text(64), hash = r.Text(64);
        Racer[] racers = new Racer[Math.Min((int)r.U8(), 16)];
        for (int i = 0; i < racers.Length; i++) racers[i] = new Racer(r.U8(), r.Text(NameChars), r.Text(64), r.U8(), r.Text(64));
        return new Roster(course, hash, racers);
    }

    /// <summary>The most pieces one <see cref="Need"/> asks for: 400 is 803 bytes.</summary>
    public const int MostNeeded = 400;

    private static Need ReadNeed(ref Reader r)
    {
        uint transfer = r.U32();
        ushort[] missing = new ushort[Math.Min((int)r.U16(), MostNeeded)];
        for (int i = 0; i < missing.Length; i++) missing[i] = r.U16();
        return new Need(transfer, missing);
    }

    // A state with anything not a number in it would put a car nowhere.
    private static bool Finite(State s) =>
        double.IsFinite(s.Sent) && double.IsFinite(s.Pose.S) && double.IsFinite(s.Pose.LengthM) && Vec.IsFinite(s.Pose.Offset)
        && Vec.IsFinite(s.Pose.Velocity) && Vec.IsFinite(s.Pose.SpinBody) && double.IsFinite(s.Pose.Attitude.W)
        && s.Pose.Attitude.LengthSquared() > 0.5 && float.IsFinite(s.Throttle) && float.IsFinite(s.Steer) && float.IsFinite(s.Brake);

    private ref struct Writer(Span<byte> into)
    {
        private readonly Span<byte> _into = into;
        public int At { get; private set; }

        public void Bytes(ReadOnlySpan<byte> b)
        {
            b.CopyTo(_into[At..]);
            At += b.Length;
        }

        public void U8(byte v) => _into[At++] = v;

        public void U16(ushort v)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(_into[At..], v);
            At += 2;
        }

        public void U32(uint v)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_into[At..], v);
            At += 4;
        }

        public void F32(float v)
        {
            BinaryPrimitives.WriteSingleLittleEndian(_into[At..], v);
            At += 4;
        }

        public void F64(double v)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(_into[At..], v);
            At += 8;
        }

        public void V3(double3 v)
        {
            F32((float)v.X);
            F32((float)v.Y);
            F32((float)v.Z);
        }

        // Two length bytes and UTF-8, for what can be longer than a byte says.
        public void LongText(string? s, int chars)
        {
            s ??= "";
            if (s.Length > chars) s = s[..chars];
            int n = Encoding.UTF8.GetByteCount(s);
            U16((ushort)n);
            At += Encoding.UTF8.GetBytes(s, _into[At..]);
        }

        // A length byte and UTF-8, cut to so many characters and never mid-character.
        public void Text(string? s, int chars)
        {
            s ??= "";
            if (s.Length > chars) s = s[..chars];
            int n = Encoding.UTF8.GetByteCount(s);
            while (n > 255)
            {
                s = s[..^1];
                n = Encoding.UTF8.GetByteCount(s);
            }
            U8((byte)n);
            At += Encoding.UTF8.GetBytes(s, _into[At..]);
        }
    }

    private ref struct Reader(ReadOnlySpan<byte> from)
    {
        private readonly ReadOnlySpan<byte> _from = from;
        private int _at;

        public ReadOnlySpan<byte> Take(int n)
        {
            if (_at + n > _from.Length) throw new ArgumentOutOfRangeException(nameof(n));
            ReadOnlySpan<byte> b = _from.Slice(_at, n);
            _at += n;
            return b;
        }

        public byte U8() => Take(1)[0];
        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
        public double F64() => BinaryPrimitives.ReadDoubleLittleEndian(Take(8));
        public double3 V3() => new(F32(), F32(), F32());

        public string LongText(int chars)
        {
            string s = Encoding.UTF8.GetString(Take(U16()));
            return s.Length > chars ? s[..chars] : s;
        }

        public string Text(int chars)
        {
            string s = Encoding.UTF8.GetString(Take(U8()));
            return s.Length > chars ? s[..chars] : s;
        }
    }
}
