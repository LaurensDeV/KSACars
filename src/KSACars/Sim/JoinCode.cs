using System.Net;
using System.Net.Sockets;

namespace KSACars;

/// <summary>
/// An address a player is reached at, as a code of ten letters and digits to read out or paste: an
/// IPv4 address and a port are 48 bits, which is ten characters of Crockford's base 32, in two fives.
/// Its alphabet has no I, L, O or U, and reading one back takes I and L for 1 and O for 0, so a code
/// copied by hand still reads. A plain <c>host:port</c> is taken too, for a tunnel's or a VPN's address.
/// </summary>
internal static class JoinCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Of(IPEndPoint at)
    {
        byte[] ip = at.Address.MapToIPv4().GetAddressBytes();
        ulong bits = ((ulong)ip[0] << 40) | ((ulong)ip[1] << 32) | ((ulong)ip[2] << 24) | ((ulong)ip[3] << 16) | (ushort)at.Port;
        Span<char> c = stackalloc char[11];
        for (int i = 9; i >= 0; i--)
        {
            c[i < 5 ? i : i + 1] = Alphabet[(int)(bits & 31)];
            bits >>= 5;
        }
        c[5] = '-';
        return new string(c);
    }

    /// <summary>A code, or a host and port, read: the host is a name to be looked up or an address. Null with <paramref name="why"/> for neither.</summary>
    public static (string Host, int Port)? Read(string? text, out string why)
    {
        why = "";
        string t = (text ?? "").Trim();
        if (t.Length == 0)
        {
            why = "nothing was given";
            return null;
        }

        // An address with a port: the last colon, so an IPv6 address in brackets keeps its own.
        int colon = t.LastIndexOf(':');
        if (colon > 0 && int.TryParse(t[(colon + 1)..], out int port))
        {
            string host = t[..colon].Trim('[', ']');
            if (port is < 1 or > 65535 || host.Length == 0)
            {
                why = $"'{t}' is not a host and a port";
                return null;
            }
            return (host, port);
        }

        ulong bits = 0;
        int n = 0;
        foreach (char raw in t.ToUpperInvariant())
        {
            if (raw is '-' or ' ') continue;
            char ch = raw switch { 'O' => '0', 'I' or 'L' => '1', _ => raw };
            int v = Alphabet.IndexOf(ch);
            if (v < 0)
            {
                why = $"'{raw}' is not in a code";
                return null;
            }
            bits = (bits << 5) | (uint)v;
            n++;
        }
        if (n != 10)
        {
            why = $"a code is ten letters and digits, and that is {n}";
            return null;
        }
        if (bits >> 48 != 0)
        {
            why = "that is not a code";
            return null;
        }
        IPAddress address = new([(byte)(bits >> 40), (byte)(bits >> 32), (byte)(bits >> 24), (byte)(bits >> 16)]);
        int p = (int)(bits & 0xFFFF);
        if (p == 0)
        {
            why = "that code has no port";
            return null;
        }
        return (address.ToString(), p);
    }

    /// <summary>Whether an address is in the range Tailscale hands its machines, 100.64.0.0/10.</summary>
    public static bool IsTailscale(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] b = address.GetAddressBytes();
        return b[0] == 100 && b[1] is >= 64 and <= 127;
    }

    /// <summary>The first of a machine's own addresses that is a Tailscale one, or null.</summary>
    public static IPAddress? TailscaleOf(IEnumerable<IPAddress> own) => own.FirstOrDefault(IsTailscale);

    /// <summary>Whether an address is one only its own network reaches, for saying a code will not work from outside.</summary>
    public static bool IsPrivate(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return IPAddress.IsLoopback(address);
        byte[] b = address.GetAddressBytes();
        return b[0] == 10 || b[0] == 127 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168)
               || (b[0] == 169 && b[1] == 254) || IsTailscale(address);
    }
}
