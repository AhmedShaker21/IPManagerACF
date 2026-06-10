using System.Net;

namespace IpManager.Core.Util;

/// <summary>
/// IPv4 helpers. The whole point of <see cref="ToNumeric"/> is correct sorting:
/// text sorting puts "192.168.1.10" before "192.168.1.9"; numeric sorting fixes it.
/// </summary>
public static class IpHelper
{
    /// <summary>"192.168.1.10" -> 3232235786.</summary>
    public static long ToNumeric(string ip)
    {
        var bytes = IPAddress.Parse(ip.Trim()).GetAddressBytes(); // big-endian for IPv4
        if (bytes.Length != 4)
            throw new ArgumentException("IPv4 only", nameof(ip));
        return ((long)bytes[0] << 24) | ((long)bytes[1] << 16)
             | ((long)bytes[2] << 8) | bytes[3];
    }

    public static string FromNumeric(long n) =>
        $"{(n >> 24) & 0xFF}.{(n >> 16) & 0xFF}.{(n >> 8) & 0xFF}.{n & 0xFF}";

    public static int LastOctet(string ip) => (int)(ToNumeric(ip) & 0xFF);

    /// <summary>Usable host addresses inside a CIDR, e.g. "192.168.1.0/24" -> .1 .. .254.</summary>
    public static IEnumerable<string> EnumerateHosts(string cidr)
    {
        var parts = cidr.Split('/', StringSplitOptions.TrimEntries);
        long network = ToNumeric(parts[0]);
        int prefix = parts.Length > 1 ? int.Parse(parts[1]) : 32;
        int hostBits = 32 - prefix;
        long size = hostBits >= 31 ? 0 : 1L << hostBits;
        long start = network & ~(size == 0 ? 0 : size - 1);

        if (prefix >= 31) { yield return FromNumeric(network); yield break; }

        for (long i = 1; i < size - 1; i++)       // skip network (.0) and broadcast (.255)
            yield return FromNumeric(start + i);
    }
}
