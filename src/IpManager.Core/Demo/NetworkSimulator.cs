using IpManager.Core.Abstractions;
using IpManager.Core.Dtos;
using IpManager.Core.Enums;
using IpManager.Core.Options;
using IpManager.Core.Util;
using Microsoft.Extensions.Options;

namespace IpManager.Core.Demo;

/// <summary>
/// Self-driving sample network for مصنع الطائرات. It does NOT bypass the system: it produces
/// the same <see cref="NetworkObservation"/> / <see cref="InternetEvent"/> a real collector would,
/// and pushes them through <see cref="INetworkService"/>, so every feature (used/free/conflict/
/// internet/notifications/live grid) exercises the genuine code path.
/// </summary>
public sealed class NetworkSimulator
{
    private readonly INetworkService _net;
    private readonly NetworkOptions _opts;
    private readonly Random _rng;

    private readonly List<string> _hosts = new();                 // all assignable host IPs
    private readonly Dictionary<string, SimDevice> _pool = new();  // mac -> device metadata
    private readonly Dictionary<string, string> _assigned = new(); // ip -> mac (primary occupant)
    private readonly Dictionary<string, string> _conflictExtra = new(); // ip -> second mac (the clash)
    private readonly HashSet<string> _online = new();             // ips currently using internet

    private static readonly string[] Types = { "PC", "PC", "PC", "Phone", "Printer", "AP", "Server", "IoT" };
    private static readonly string[] Depts = { "Avionics", "Airframe", "Quality", "IT", "Logistics", "Engineering", "Admin", "Composites" };
    private static readonly string[] Bldgs = { "Hangar A", "Hangar B", "Bldg 3", "Bldg 7", "Annex", "Flight Line" };
    private static readonly string[] Oui = { "F4:8E:38", "00:1B:44", "AC:DE:48", "B8:27:EB", "3C:5A:B4", "00:50:56", "D8:9E:F3", "E4:5F:01" };

    public NetworkSimulator(INetworkService net, IOptions<NetworkOptions> opts)
    {
        _net = net;
        _opts = opts.Value;
        _rng = new Random(_opts.Demo.Seed);
        foreach (var cidr in _opts.Subnets)
            _hosts.AddRange(IpHelper.EnumerateHosts(cidr));
        BuildPool(Math.Min(_opts.Demo.SeedDevices * 3, Math.Max(60, _hosts.Count / 2)));
    }

    private record SimDevice(string Mac, string Hostname, string Type, string Dept, string Location);

    private void BuildPool(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var type = Types[_rng.Next(Types.Length)];
            var mac = $"{Oui[_rng.Next(Oui.Length)]}:{_rng.Next(256):X2}:{_rng.Next(256):X2}:{_rng.Next(256):X2}";
            var prefix = type switch { "Printer" => "PRN", "AP" => "AP", "Server" => "SRV", "Phone" => "VOIP", "IoT" => "IOT", _ => "PC" };
            var dev = new SimDevice(
                mac,
                $"AF-{prefix}-{i:D4}",
                type,
                Depts[_rng.Next(Depts.Length)],
                $"{Bldgs[_rng.Next(Bldgs.Length)]} / Sw-{_rng.Next(1, 9)} / Gi1/0/{_rng.Next(1, 48)}");
            _pool[mac] = dev;
        }
    }

    public async Task SeedAsync()
    {
        _net.EnsureSeeded();
        int target = Math.Min(_opts.Demo.SeedDevices, _hosts.Count - 2);
        var macs = _pool.Keys.ToList();
        for (int i = 0; i < target; i++)
        {
            var ip = FreeIp();
            if (ip is null) break;
            _assigned[ip] = macs[i % macs.Count];
        }
        await PushCycleAsync();

        // put roughly half of them online
        foreach (var ip in _assigned.Keys.Where(_ => _rng.NextDouble() < 0.5).ToList())
            await GoOnlineAsync(ip);

        // seed a couple of live conflicts so the board is interesting on first load
        StartConflict();
        StartConflict();
        await PushCycleAsync();
    }

    public async Task TickAsync()
    {
        double r = _rng.NextDouble();

        if (r < 0.30) AddDevice();
        else if (r < 0.50) RemoveDevice();
        else if (r < 0.66) StartConflict();
        else if (r < 0.72) ResolveConflict();

        await PushCycleAsync();

        // some online churn every tick
        foreach (var ip in _assigned.Keys.Where(_ => _rng.NextDouble() < 0.25).Take(4).ToList())
            await GoOnlineAsync(ip);
    }

    // --- mutations to the simulated "truth" -----------------------------------

    private void AddDevice()
    {
        var ip = FreeIp();
        var mac = _pool.Keys.FirstOrDefault(m => !_assigned.ContainsValue(m));
        if (ip is null || mac is null) return;
        _assigned[ip] = mac;
    }

    private void RemoveDevice()
    {
        if (_assigned.Count == 0) return;
        var ip = _assigned.Keys.ElementAt(_rng.Next(_assigned.Count));
        _assigned.Remove(ip);
        _conflictExtra.Remove(ip);
        _online.Remove(ip);
    }

    private void StartConflict()
    {
        var occupied = _assigned.Keys.Where(ip => !_conflictExtra.ContainsKey(ip)).ToList();
        if (occupied.Count == 0) return;
        var ip = occupied[_rng.Next(occupied.Count)];
        var clashing = _pool.Keys.FirstOrDefault(m => !_assigned.ContainsValue(m) && !_conflictExtra.ContainsValue(m));
        if (clashing is null) return;
        _conflictExtra[ip] = clashing;
    }

    private void ResolveConflict()
    {
        if (_conflictExtra.Count == 0) return;
        var ip = _conflictExtra.Keys.ElementAt(_rng.Next(_conflictExtra.Count));
        _conflictExtra.Remove(ip);
    }

    // --- push the current truth through the real pipeline ---------------------

    private async Task PushCycleAsync()
    {
        var observations = new List<NetworkObservation>();
        var seen = new HashSet<string>();
        var now = DateTime.UtcNow;

        foreach (var (ip, mac) in _assigned)
        {
            observations.Add(Observe(ip, mac, now));
            seen.Add(ip);
        }
        foreach (var (ip, mac) in _conflictExtra)
        {
            observations.Add(Observe(ip, mac, now));
            seen.Add(ip);
        }

        await _net.RunScanCycleAsync(observations, seen);
    }

    private NetworkObservation Observe(string ip, string mac, DateTime now)
    {
        var d = _pool[mac];
        // short lease: a device that stops being observed frees its IP within a few ticks
        var lease = now.AddSeconds(Math.Max(12, _opts.Demo.TickSeconds * 2));
        return new NetworkObservation(ip, d.Mac, d.Hostname, d.Type, d.Dept, d.Location,
            BindingSource.Simulated, lease, now);
    }

    private async Task GoOnlineAsync(string ip)
    {
        if (!_assigned.TryGetValue(ip, out _)) return;
        _online.Add(ip);
        await _net.ApplyInternetEventAsync(new InternetEvent(
            ip, DateTime.UtcNow,
            $"{_rng.Next(1, 224)}.{_rng.Next(256)}.{_rng.Next(256)}.{_rng.Next(256)}",
            _rng.Next(2_000, 9_000_000), _rng.Next(500, 2_000_000), "Demo"));
    }

    private string? FreeIp()
    {
        var taken = new HashSet<string>(_assigned.Keys);
        var candidates = _hosts.Where(h => !taken.Contains(h)).ToList();
        return candidates.Count == 0 ? null : candidates[_rng.Next(candidates.Count)];
    }
}
