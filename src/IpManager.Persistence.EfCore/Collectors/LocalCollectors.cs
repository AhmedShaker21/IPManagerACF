using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using IpManager.Core.Abstractions;
using IpManager.Core.Util;

namespace IpManager.Persistence.EfCore.Collectors;

/// <summary>
/// ICMP-sweeps a subnet. Two purposes: (1) learn which addresses respond, and (2) — more
/// importantly — populate THIS host's ARP cache so <see cref="ArpTableReader"/> can read the
/// IP→MAC mappings. ARP only works for the subnet(s) this machine is directly attached to;
/// for other subnets you need <see cref="SnmpArpReader"/> or DHCP.
/// </summary>
public sealed class PingSweepScanner : INetworkScanner
{
    public async Task<IReadOnlyList<string>> SweepAsync(string cidr, CancellationToken ct)
    {
        var alive = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var gate = new SemaphoreSlim(64); // bound concurrency

        var tasks = IpHelper.EnumerateHosts(cidr).Select(async addr =>
        {
            await gate.WaitAsync(ct);
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(addr, 600);
                if (reply.Status == IPStatus.Success) alive.Add(addr);
            }
            catch { /* host unreachable */ }
            finally { gate.Release(); }
        });

        await Task.WhenAll(tasks);
        return alive.ToList();
    }
}

/// <summary>
/// Reads the local OS ARP cache (`arp -a`) → same-subnet IP↔MAC pairs. Works on Windows and Linux.
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
            OperatingSystem.IsWindows() ? "-a" : "-n", ct);

        var list = new List<HostEntry>();
        foreach (Match m in Line.Matches(text))
        {
            var mac = m.Groups["mac"].Value.Replace('-', ':').ToUpperInvariant();
            if (mac is "00:00:00:00:00:00" or "FF:FF:FF:FF:FF:FF") continue;
            list.Add(new HostEntry(m.Groups["ip"].Value, mac, null));
        }
        return list;
    }
}
