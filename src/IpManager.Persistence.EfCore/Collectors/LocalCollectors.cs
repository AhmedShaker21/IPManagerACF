using System.Net;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using IpManager.Core.Abstractions;
using IpManager.Core.Util;

namespace IpManager.Persistence.EfCore.Collectors;

/// <summary>
/// ICMP-sweeps a subnet.
/// Two purposes:
/// 1) learn which addresses respond
/// 2) populate this host's ARP cache so ArpTableReader can read IP → MAC mappings
/// </summary>
public sealed class PingSweepScanner : INetworkScanner
{
    public async Task<IReadOnlyList<string>> SweepAsync(string cidr, CancellationToken ct)
    {
        var alive = new System.Collections.Concurrent.ConcurrentBag<string>();

        using var gate = new SemaphoreSlim(64);

        var tasks = IpHelper.EnumerateHosts(cidr).Select(async addr =>
        {
            await gate.WaitAsync(ct);

            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(addr, 600);

                if (reply.Status == IPStatus.Success)
                    alive.Add(addr);
            }
            catch
            {
                // host unreachable
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);

        return alive.ToList();
    }
}

/// <summary>
/// Reads the local OS ARP cache: arp -a
/// This gives same-subnet IP ↔ MAC pairs.
/// </summary>
public sealed class ArpTableReader : IArpReader
{
    private static readonly Regex Line = new(
        @"(?<ip>\d{1,3}(\.\d{1,3}){3}).*?(?<mac>([0-9a-fA-F]{2}[-:]){5}[0-9a-fA-F]{2})",
        RegexOptions.Compiled);

    public async Task<IReadOnlyList<HostEntry>> ReadAsync(CancellationToken ct)
    {
        var text = await ProcessRunner.RunAsync(
            OperatingSystem.IsWindows() ? "arp" : "/usr/sbin/arp",
            OperatingSystem.IsWindows() ? "-a" : "-n",
            ct);

        var list = new List<HostEntry>();

        foreach (Match m in Line.Matches(text))
        {
            var ip = m.Groups["ip"].Value;
            var mac = m.Groups["mac"].Value.Replace('-', ':').ToUpperInvariant();

            if (!IsUsableHostIp(ip))
                continue;

            if (!IsUsableMac(mac))
                continue;

            list.Add(new HostEntry(ip, mac, null));
        }

        return list;
    }

    private static bool IsUsableHostIp(string ipText)
    {
        if (!IPAddress.TryParse(ipText, out var ip))
            return false;

        var b = ip.GetAddressBytes();

        if (b.Length != 4)
            return false;

        // 0.0.0.0
        if (b[0] == 0)
            return false;

        // 127.0.0.0/8 loopback
        if (b[0] == 127)
            return false;

        // 169.254.0.0/16 APIPA
        if (b[0] == 169 && b[1] == 254)
            return false;

        // 224.0.0.0/4 multicast
        // Examples: 224.0.0.22, 224.0.0.251, 224.0.0.252
        if (b[0] >= 224 && b[0] <= 239)
            return false;

        // 255.255.255.255 broadcast
        if (b[0] == 255 && b[1] == 255 && b[2] == 255 && b[3] == 255)
            return false;

        return true;
    }

    private static bool IsUsableMac(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac))
            return false;

        if (mac is "00:00:00:00:00:00" or "FF:FF:FF:FF:FF:FF")
            return false;

        // Multicast MACs used with 224.0.0.x addresses
        if (mac.StartsWith("01:00:5E", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}