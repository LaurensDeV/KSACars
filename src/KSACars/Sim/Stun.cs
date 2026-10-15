using System.Buffers.Binary;
using System.Net;

namespace KSACars;

/// <summary>
/// A STUN binding request and its answer (RFC 5389): asked of a public server from the socket a
/// race is run on, it says the address and port that socket is seen at from outside every router
/// in the way, which is what a player gives another to be reached at. No account and nothing
/// installed: the servers are free and public.
/// </summary>
internal static class Stun
{
    private const ushort BindingRequest = 0x0001, BindingSuccess = 0x0101;
    private const ushort MappedAddress = 0x0001, XorMappedAddress = 0x0020;
    private const uint MagicCookie = 0x2112A442;

    /// <summary>Public servers, asked in turn until one answers.</summary>
    public static readonly (string Host, int Port)[] Servers =
    [
        ("stun.l.google.com", 19302),
        ("stun1.l.google.com", 19302),
        ("stun.cloudflare.com", 3478),
    ];

    /// <summary>A binding request with the transaction it is to be answered by.</summary>
    public static byte[] Request(ReadOnlySpan<byte> transaction)
    {
        byte[] b = new byte[20];
        BinaryPrimitives.WriteUInt16BigEndian(b, BindingRequest);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(2), 0);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4), MagicCookie);
        transaction[..12].CopyTo(b.AsSpan(8));
        return b;
    }

    /// <summary>Whether a datagram is a STUN message at all, so a race's socket can tell one from its own.</summary>
    public static bool IsStun(ReadOnlySpan<byte> data) =>
        data.Length >= 20 && (data[0] & 0xC0) == 0 && BinaryPrimitives.ReadUInt32BigEndian(data[4..]) == MagicCookie;

    /// <summary>The address a binding success for this transaction says the asker is seen at, or null.</summary>
    public static IPEndPoint? Answer(ReadOnlySpan<byte> data, ReadOnlySpan<byte> transaction)
    {
        if (!IsStun(data) || BinaryPrimitives.ReadUInt16BigEndian(data) != BindingSuccess) return null;
        if (!data.Slice(8, 12).SequenceEqual(transaction[..12])) return null;
        int length = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
        if (20 + length > data.Length) return null;

        IPEndPoint? plain = null;
        int at = 20;
        while (at + 4 <= 20 + length)
        {
            ushort type = BinaryPrimitives.ReadUInt16BigEndian(data[at..]);
            int size = BinaryPrimitives.ReadUInt16BigEndian(data[(at + 2)..]);
            ReadOnlySpan<byte> value = at + 4 + size <= data.Length ? data.Slice(at + 4, size) : default;
            if (value.Length >= 8 && value[1] == 0x01)
            {
                ushort port = BinaryPrimitives.ReadUInt16BigEndian(value[2..]);
                uint address = BinaryPrimitives.ReadUInt32BigEndian(value[4..]);
                if (type == XorMappedAddress)
                {
                    port ^= (ushort)(MagicCookie >> 16);
                    address ^= MagicCookie;
                    return new IPEndPoint(new IPAddress(Big(address)), port);
                }
                if (type == MappedAddress) plain = new IPEndPoint(new IPAddress(Big(address)), port);
            }
            // Each attribute is padded to four bytes.
            at += 4 + ((size + 3) & ~3);
        }
        return plain;
    }

    private static byte[] Big(uint v)
    {
        byte[] b = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, v);
        return b;
    }
}
