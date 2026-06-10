using System.Net.NetworkInformation;
using System.Net.Sockets;
using IpManager.Core.Abstractions;
using IpManager.Core.Dtos;
using IpManager.Core.Enums;
using IpManager.Core.Options;
using Microsoft.Extensions.Options;

namespace IpManager.Web.Workers;

/// <summary>
/// Live discovery:
/// 1) ping-sweep each subnet to populate ARP cache
/// 2) read local ARP table
/// 3) read SNMP ARP tables if configured
/// 4) add the collector machine itself
/// </summary>
public sealed class NetworkScanWorker : BackgroundService
{
    private readonly INetworkService _net;
    private readonly NetworkOptions _opts;
    private readonly IEnumerable<INetworkScanner> _scanners;
    private readonly IEnumerable<IArpReader> _arp;
    private readonly IEnumerable<ISnmpArpReader> _snmp;
    private readonly ILogger<NetworkScanWorker> _log;

    public NetworkScanWorker(
        INetworkService net,
        IOptions<NetworkOptions> opts,
        IEnumerable<INetworkScanner> scanners,
        IEnumerable<IArpReader> arp,
        IEnumerable<ISnmpArpReader> snmp,
        ILogger<NetworkScanWorker> log)
    {
        _net = net;
        _opts = opts.Value;
        _scanners = scanners;
        _arp = arp;
        _snmp = snmp;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        _net.EnsureSeeded();

        if (!_scanners.Any() && !_arp.Any() && !_snmp.Any())
        {
            _log.LogWarning(
                "Live mode but no scan collectors registered. Add AddLiveCollectors() from IpManager.Persistence.EfCore.");
        }

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(Math.Max(10, _opts.ScanIntervalSeconds)));

        do
        {
            try
            {
                await ScanOnceAsync(stop);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Scan cycle failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stop));
    }

    private async Task ScanOnceAsync(CancellationToken stop)
    {
        var scanner = _scanners.FirstOrDefault();

        if (scanner is not null)
        {
            foreach (var subnet in _opts.Subnets)
            {
                await scanner.SweepAsync(subnet, stop);
            }
        }

        var observations = new List<NetworkObservation>();
        var seen = new HashSet<string>();
        var now = DateTime.UtcNow;

        foreach (var reader in _arp)
        {
            foreach (var h in await reader.ReadAsync(stop))
            {
                Add(observations, seen, h, BindingSource.Arp, now);
            }
        }

        foreach (var reader in _snmp)
        {
            foreach (var h in await reader.ReadAsync(stop))
            {
                Add(observations, seen, h, BindingSource.Snmp, now);
            }
        }

        AddLocalMachine(observations, seen, now);

        if (observations.Count > 0)
        {
            await _net.RunScanCycleAsync(observations, seen);
        }
    }

    private static void Add(
        List<NetworkObservation> list,
        HashSet<string> seen,
        HostEntry h,
        BindingSource src,
        DateTime now)
    {
        if (string.IsNullOrWhiteSpace(h.Ip) || string.IsNullOrWhiteSpace(h.Mac))
            return;

        list.Add(new NetworkObservation(
            h.Ip,
            h.Mac,
            h.Hostname,
            null,
            null,
            null,
            src,
            null,
            now));

        seen.Add(h.Ip);
    }

    private static void AddLocalMachine(
        List<NetworkObservation> list,
        HashSet<string> seen,
        DateTime now)
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;

            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            var macBytes = ni.GetPhysicalAddress().GetAddressBytes();

            if (macBytes.Length == 0)
                continue;

            var mac = string.Join(":", macBytes.Select(b => b.ToString("X2")));

            foreach (var addr in ni.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                var ip = addr.Address.ToString();

                if (ip.StartsWith("127."))
                    continue;

                if (ip.StartsWith("169.254."))
                    continue;

                list.Add(new NetworkObservation(
                    ip,
                    mac,
                    Environment.MachineName,
                    "Collector",
                    null,
                    "This machine",
                    BindingSource.Arp,
                    null,
                    now));

                seen.Add(ip);
            }
        }
    }
}

/// <summary>
/// Pulls richer bindings from the DHCP server.
/// DHCP gives IP + MAC + hostname + lease expiry.
/// </summary>
public sealed class DhcpSyncWorker : BackgroundService
{
    private readonly INetworkService _net;
    private readonly NetworkOptions _opts;
    private readonly IEnumerable<IDhcpLeaseReader> _readers;
    private readonly ILogger<DhcpSyncWorker> _log;

    public DhcpSyncWorker(
        INetworkService net,
        IOptions<NetworkOptions> opts,
        IEnumerable<IDhcpLeaseReader> readers,
        ILogger<DhcpSyncWorker> log)
    {
        _net = net;
        _opts = opts.Value;
        _readers = readers;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!_readers.Any())
            return;

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(Math.Max(15, _opts.DhcpIntervalSeconds)));

        do
        {
            try
            {
                foreach (var reader in _readers)
                {
                    foreach (var lease in await reader.ReadAsync(stop))
                    {
                        if (!lease.Active)
                            continue;

                        await _net.ApplyObservationAsync(new NetworkObservation(
                            lease.Ip,
                            lease.Mac,
                            lease.Hostname,
                            null,
                            null,
                            null,
                            BindingSource.Dhcp,
                            lease.ExpiresAt,
                            DateTime.UtcNow));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "DHCP sync failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stop));
    }
}

/// <summary>
/// Consumes the gateway's internet-usage stream and attributes each event to a device.
/// </summary>
public sealed class InternetActivityWorker : BackgroundService
{
    private readonly INetworkService _net;
    private readonly IEnumerable<IInternetActivityReader> _readers;
    private readonly ILogger<InternetActivityWorker> _log;

    public InternetActivityWorker(
        INetworkService net,
        IEnumerable<IInternetActivityReader> readers,
        ILogger<InternetActivityWorker> log)
    {
        _net = net;
        _readers = readers;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var reader = _readers.FirstOrDefault();

        if (reader is null)
            return;

        try
        {
            await foreach (var ev in reader.ReadAsync(stop))
            {
                await _net.ApplyInternetEventAsync(ev);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Internet activity reader stopped");
        }
    }
}