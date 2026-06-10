using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using IpManager.Core.Abstractions;
using IpManager.Core.Dtos;
using IpManager.Core.Options;
using IpManager.Core.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IpManager.Persistence.EfCore.Collectors;

/// <summary>
/// THE per-device internet mechanism.
///
/// A device's internet traffic never passes through this application's server — it goes
/// device → gateway → internet. So the app cannot "see" usage directly; it has to be TOLD by the
/// box that does sit in the path: the firewall / NAT gateway / proxy. The standard way that box
/// reports sessions is by streaming syslog (or NetFlow/IPFIX) records, one per connection, each
/// carrying source IP, destination IP, and byte counts.
///
/// This reader listens for those records over UDP. For each record it pulls the internal source IP
/// and the destination IP; if the destination is NOT inside the configured internal LAN ranges, the
/// session is internet-bound, and we emit an <see cref="InternetEvent"/> keyed by the internal IP.
/// The store then attributes it to whichever device held that IP at the moment of the session
/// (IP → active binding at timestamp → device), which is why short-lived DHCP leases still resolve
/// to the right machine.
///
/// The regex below is deliberately permissive (it locates "a=b.c.d.e ... =N" style fields common to
/// Fortinet/pfSense/Palo Alto syslog). Point it at your firewall's exact field names if needed.
/// </summary>
public sealed class SyslogInternetActivityReader : IInternetActivityReader
{
    private readonly SyslogOptions _opts;
    private readonly ILogger<SyslogInternetActivityReader> _log;
    private readonly (long net, long mask)[] _internal;

    private static readonly Regex SrcIp = new(@"\bsrc(?:ip)?[=:\s]+(?<v>\d{1,3}(\.\d{1,3}){3})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DstIp = new(@"\bdst(?:ip)?[=:\s]+(?<v>\d{1,3}(\.\d{1,3}){3})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SentB = new(@"\b(?:sentbyte|bytes_out|txbytes|sent)[=:\s]+(?<v>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RcvdB = new(@"\b(?:rcvdbyte|bytes_in|rxbytes|rcvd)[=:\s]+(?<v>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public SyslogInternetActivityReader(IOptions<NetworkOptions> opts, ILogger<SyslogInternetActivityReader> log)
    {
        _opts = opts.Value.Syslog;
        _log = log;
        _internal = _opts.InternalRanges.Select(ParseCidr).ToArray();
    }

    public async IAsyncEnumerable<InternetEvent> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        using var udp = new UdpClient(_opts.UdpPort);
        _log.LogInformation("Listening for firewall/flow syslog on UDP {Port}.", _opts.UdpPort);

        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult res;
            bool stop = false;
            try { res = await udp.ReceiveAsync(ct); }
            catch (OperationCanceledException) { res = default; stop = true; }
            catch (Exception ex) { _log.LogWarning(ex, "syslog receive error"); continue; }
            if (stop) yield break;

            var line = Encoding.UTF8.GetString(res.Buffer);
            var ev = Parse(line);
            if (ev is not null) yield return ev;
        }
    }

    private InternetEvent? Parse(string line)
    {
        var src = SrcIp.Match(line);
        var dst = DstIp.Match(line);
        if (!src.Success || !dst.Success) return null;

        var srcIp = src.Groups["v"].Value;
        var dstIp = dst.Groups["v"].Value;

        // internal→internal traffic is not "internet usage"
        if (IsInternal(dstIp)) return null;

        long.TryParse(SentB.Match(line).Groups["v"].Value, out var sent);
        long.TryParse(RcvdB.Match(line).Groups["v"].Value, out var rcvd);

        return new InternetEvent(srcIp, DateTime.UtcNow, dstIp, BytesIn: rcvd, BytesOut: sent, Source: "Firewall");
    }

    private bool IsInternal(string ip)
    {
        long v;
        try { v = IpHelper.ToNumeric(ip); } catch { return false; }
        return _internal.Any(r => (v & r.mask) == (r.net & r.mask));
    }

    private static (long net, long mask) ParseCidr(string cidr)
    {
        var parts = cidr.Split('/');
        long net = IpHelper.ToNumeric(parts[0]);
        int prefix = parts.Length > 1 ? int.Parse(parts[1]) : 32;
        long mask = prefix == 0 ? 0 : ~((1L << (32 - prefix)) - 1) & 0xFFFFFFFF;
        return (net, mask);
    }
}
