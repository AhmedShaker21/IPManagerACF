using System.Net;
using IpManager.Core.Abstractions;
using IpManager.Core.Options;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IpManager.Persistence.EfCore.Collectors;

/// <summary>
/// THE cross-subnet MAC mechanism.
///
/// A MAC address only travels inside a single layer-2 segment, so a server sitting in
/// 192.168.1.0/24 can never learn (via ARP) the MAC of a host in 192.168.5.0/24 — the router
/// strips the L2 header when it forwards between subnets. The one device that DOES know the MACs
/// of every subnet is the router / layer-3 switch: it keeps an ARP table per interface.
///
/// We read that table over SNMP. The relevant table is ipNetToMediaTable; the column we want is
/// ipNetToMediaPhysAddress = OID 1.3.6.1.2.1.4.22.1.2. Each row's OID suffix encodes
/// "&lt;ifIndex&gt;.&lt;a&gt;.&lt;b&gt;.&lt;c&gt;.&lt;d&gt;" (the last four numbers are the IP) and the value is the
/// 6-byte MAC. Walking it on each gateway yields IP→MAC for every directly-connected subnet.
/// </summary>
public sealed class SnmpArpReader : ISnmpArpReader
{
    // ipNetToMediaPhysAddress — the router's per-interface ARP cache (IP -> MAC).
    private const string IpNetToMediaPhysAddress = "1.3.6.1.2.1.4.22.1.2";

    private readonly SnmpOptions _opts;
    private readonly ILogger<SnmpArpReader> _log;

    public SnmpArpReader(IOptions<NetworkOptions> opts, ILogger<SnmpArpReader> log)
    {
        _opts = opts.Value.Snmp;
        _log = log;
    }

    public Task<IReadOnlyList<HostEntry>> ReadAsync(CancellationToken ct)
    {
        var entries = new List<HostEntry>();

        foreach (var host in _opts.RouterHosts)
        {
            try
            {
                var rows = new List<Variable>();
                Messenger.Walk(
                    VersionCode.V2,
                    new IPEndPoint(IPAddress.Parse(host), _opts.Port),
                    new OctetString(_opts.Community),
                    new ObjectIdentifier(IpNetToMediaPhysAddress),
                    rows,
                    _opts.TimeoutMs,
                    WalkMode.WithinSubtree);

                foreach (var row in rows)
                {
                    var ip = IpFromOidSuffix(row.Id.ToString());
                    var mac = MacFromValue(row.Data);
                    if (ip is null || mac is null) continue;
                    if (mac is "00:00:00:00:00:00") continue;
                    entries.Add(new HostEntry(ip, mac, null));
                }

                _log.LogInformation("SNMP walk of {Host} returned {Count} ARP entries.", host, rows.Count);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "SNMP walk of {Host} failed. Check community string, ACL, and that SNMP is enabled.", host);
            }
        }

        return Task.FromResult<IReadOnlyList<HostEntry>>(entries);
    }

    // OID suffix is "...4.22.1.2.<ifIndex>.<a>.<b>.<c>.<d>" — last four parts are the IPv4 address.
    private static string? IpFromOidSuffix(string oid)
    {
        var parts = oid.Split('.');
        if (parts.Length < 4) return null;
        var last4 = parts[^4..];
        return last4.All(p => byte.TryParse(p, out _)) ? string.Join('.', last4) : null;
    }

    private static string? MacFromValue(ISnmpData data)
    {
        if (data is not OctetString os) return null;
        var raw = os.GetRaw();
        if (raw.Length != 6) return null;
        return string.Join(':', raw.Select(b => b.ToString("X2")));
    }
}
