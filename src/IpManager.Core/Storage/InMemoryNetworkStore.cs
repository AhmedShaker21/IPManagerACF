using IpManager.Core.Abstractions;
using IpManager.Core.Dtos;
using IpManager.Core.Entities;
using IpManager.Core.Enums;
using IpManager.Core.Util;

namespace IpManager.Core.Storage;

/// <summary>
/// Thread-safe, in-memory network state. This is the default store so the app runs with
/// zero database setup. Every method mirrors what <c>EfNetworkStore</c> does against SQL Server.
/// </summary>
public sealed class InMemoryNetworkStore : INetworkStore
{
    private readonly object _gate = new();

    private readonly Dictionary<string, IpAddress> _ipByAddr = new();
    private readonly Dictionary<int, IpAddress> _ipById = new();
    private readonly Dictionary<string, Device> _devByMac = new();
    private readonly Dictionary<int, Device> _devById = new();
    private readonly List<IpBinding> _bindings = new();
    private readonly List<Conflict> _conflicts = new();
    private readonly List<InternetActivity> _activities = new();
    private readonly List<Notification> _notifications = new();
    private readonly Dictionary<int, int> _missCount = new(); // bindingId -> consecutive misses

    private int _ipSeq, _devSeq, _bindSeq, _confSeq, _actSeq, _notifSeq;

    // ---------------------------------------------------------------- seeding

    public void EnsureSeeded(IEnumerable<string> subnets)
    {
        lock (_gate)
        {
            foreach (var cidr in subnets)
            foreach (var addr in IpHelper.EnumerateHosts(cidr))
            {
                if (_ipByAddr.ContainsKey(addr)) continue;
                var ip = new IpAddress
                {
                    Id = ++_ipSeq,
                    Address = addr,
                    IpNumeric = IpHelper.ToNumeric(addr),
                    Subnet = cidr,
                    Status = IpStatus.Available
                };
                _ipByAddr[addr] = ip;
                _ipById[ip.Id] = ip;
            }
        }
    }

    // ------------------------------------------------------------- mutations

    public IReadOnlyList<DomainEvent> ApplyObservation(NetworkObservation o)
    {
        lock (_gate)
        {
            var events = new List<DomainEvent>();
            var now = o.At;

            var ip = GetOrAddIp(o.Ip);
            var mac = MacHelper.Normalize(o.Mac);
            var device = GetOrAddDevice(mac, o, now);

            // a device has at most one current IP — close its other active bindings
            foreach (var b in _bindings.Where(b => b.DeviceId == device.Id && b.IsActive && b.IpAddressId != ip.Id))
                Deactivate(b, now);

            var existing = _bindings.FirstOrDefault(b => b.IpAddressId == ip.Id && b.DeviceId == device.Id && b.IsActive);
            if (existing is null)
            {
                AddBinding(ip, device, o.Source, now, o.LeaseEnd);

                bool wasAvailable = ip.Status == IpStatus.Available;
                RecomputeIpStatus(ip, now, events);
                if (wasAvailable && ip.Status == IpStatus.Used)
                    events.Add(Raise(NotificationType.IpUsed, "IP now in use",
                        $"{ip.Address} is now used by {device.MacAddress}" +
                        (device.Hostname is { Length: > 0 } ? $" ({device.Hostname})" : ""), now, ip.Address, device.MacAddress));
            }
            else
            {
                existing.LeaseEnd = o.LeaseEnd;
                _missCount[existing.Id] = 0;
            }

            device.LastSeen = now;
            ip.LastSeen = now;
            return events;
        }
    }

    public IReadOnlyList<DomainEvent> ExpireStale(IReadOnlySet<string> seenIps, DateTime now, int missedScansBeforeFree)
    {
        lock (_gate)
        {
            var events = new List<DomainEvent>();

            foreach (var b in _bindings.Where(b => b.IsActive).ToList())
            {
                var addr = _ipById[b.IpAddressId].Address;
                bool seen = seenIps.Contains(addr);
                bool leaseExpired = b.LeaseEnd is null || b.LeaseEnd < now;

                if (seen) { _missCount[b.Id] = 0; continue; }

                _missCount[b.Id] = _missCount.GetValueOrDefault(b.Id) + 1;
                if (_missCount[b.Id] < missedScansBeforeFree || !leaseExpired) continue;

                Deactivate(b, now);
                var ip = _ipById[b.IpAddressId];
                RecomputeIpStatus(ip, now, events);
                if (ip.Status == IpStatus.Available)
                    events.Add(Raise(NotificationType.IpFreed, "IP freed",
                        $"{ip.Address} is now available.", now, ip.Address, null));
            }
            return events;
        }
    }

    public IReadOnlyList<DomainEvent> ScanConflicts()
    {
        lock (_gate)
        {
            var events = new List<DomainEvent>();
            var now = DateTime.UtcNow;

            var activeByIp = _bindings.Where(b => b.IsActive)
                                      .GroupBy(b => b.IpAddressId)
                                      .ToDictionary(g => g.Key, g => g.Select(x => x.DeviceId).Distinct().ToList());

            foreach (var (ipId, deviceIds) in activeByIp.Where(kv => kv.Value.Count > 1))
            {
                var ip = _ipById[ipId];
                ip.Status = IpStatus.Conflict;
                var open = _conflicts.FirstOrDefault(c => c.IpAddressId == ipId && !c.IsResolved);
                if (open is null)
                {
                    open = new Conflict { Id = ++_confSeq, IpAddressId = ipId, IpAddress = ip, DetectedAt = now };
                    foreach (var did in deviceIds)
                        open.Devices.Add(new ConflictDevice { ConflictId = open.Id, DeviceId = did, Device = _devById[did] });
                    _conflicts.Add(open);
                    events.Add(Raise(NotificationType.ConflictDetected, "IP conflict detected",
                        $"{ip.Address} is claimed by {deviceIds.Count} devices.", now, ip.Address, null));
                }
            }

            // resolve conflicts that are no longer conflicting
            foreach (var c in _conflicts.Where(c => !c.IsResolved).ToList())
            {
                bool still = activeByIp.TryGetValue(c.IpAddressId, out var ids) && ids.Count > 1;
                if (still) continue;
                c.IsResolved = true;
                c.ResolvedAt = now;
                RecomputeIpStatus(_ipById[c.IpAddressId], now, events);
            }
            return events;
        }
    }

    public IReadOnlyList<DomainEvent> ApplyInternetEvent(InternetEvent e)
    {        lock (_gate)
        {
            var events = new List<DomainEvent>();
            if (!_ipByAddr.TryGetValue(e.InternalIp, out var ip)) return events;

            // the device that held this IP at the moment of the activity
            var binding = _bindings
                .Where(b => b.IpAddressId == ip.Id && b.LeaseStart <= e.At && (b.LeaseEnd == null || b.LeaseEnd >= e.At))
                .OrderByDescending(b => b.LeaseStart)
                .FirstOrDefault()
                ?? _bindings.Where(b => b.IpAddressId == ip.Id && b.IsActive).OrderByDescending(b => b.LeaseStart).FirstOrDefault();
            if (binding is null) return events;

            _activities.Add(new InternetActivity
            {
                Id = ++_actSeq, DeviceId = binding.DeviceId, IpAddressId = ip.Id,
                ActivityTime = e.At, DestinationIp = e.DestinationIp,
                BytesIn = e.BytesIn, BytesOut = e.BytesOut, Source = e.Source
            });

            var device = _devById[binding.DeviceId];
            bool firstTime = !device.HasInternetAccess;
            device.HasInternetAccess = true;
            device.LastInternetActivity = e.At;
            if (firstTime)
                events.Add(Raise(NotificationType.InternetAccessStarted, "Device went online",
                    $"{device.MacAddress} ({e.InternalIp}) started using the internet.", e.At, e.InternalIp, device.MacAddress));
            return events;
        }
    }

    public IReadOnlyList<DomainEvent> AssignIp(AssignIpRequest r)
    {
        lock (_gate)
        {
            var events = new List<DomainEvent>();
            var now = DateTime.UtcNow;

            var ip = GetOrAddIp(r.Ip);

            // key the device by MAC if given, otherwise by a stable "manual" key for this IP
            var mac = string.IsNullOrWhiteSpace(r.Mac)
                ? $"MANUAL{IpHelper.ToNumeric(r.Ip):X}"
                : MacHelper.Normalize(r.Mac);

            if (!_devByMac.TryGetValue(mac, out var device))
            {
                device = new Device
                {
                    Id = ++_devSeq, MacNormalized = mac,
                    MacAddress = string.IsNullOrWhiteSpace(r.Mac) ? "(manual)" : MacHelper.ToDisplay(mac),
                    FirstSeen = now, LastSeen = now
                };
                _devByMac[mac] = device; _devById[device.Id] = device;
            }

            // overwrite the manually-entered details
            device.DeviceName = r.DeviceName ?? device.DeviceName;
            device.Hostname = r.Hostname ?? device.Hostname;
            device.DeviceType = r.DeviceType ?? device.DeviceType;
            device.Department = r.Department ?? device.Department;
            device.Location = r.Location ?? device.Location;
            device.OwnerName = r.OwnerName;
            device.OwnerEmail = r.OwnerEmail;
            device.OwnerPhone = r.OwnerPhone;
            device.Cpu = r.Cpu;
            device.RamGb = r.RamGb;
            device.StorageGb = r.StorageGb;
            device.OperatingSystem = r.OperatingSystem;
            device.AssetTag = r.AssetTag;
            device.Notes = r.Notes;
            device.IsManaged = true;
            device.LastSeen = now;

            // one IP per device: close its other active bindings
            foreach (var b in _bindings.Where(b => b.DeviceId == device.Id && b.IsActive && b.IpAddressId != ip.Id))
                Deactivate(b, now);

            var existing = _bindings.FirstOrDefault(b => b.IpAddressId == ip.Id && b.DeviceId == device.Id && b.IsActive);
            if (existing is null)
            {
                AddBinding(ip, device, BindingSource.Manual, now, null); // no expiry — a manual assignment is static
                bool wasAvailable = ip.Status == IpStatus.Available;
                RecomputeIpStatus(ip, now, events);
                if (wasAvailable && ip.Status == IpStatus.Used)
                    events.Add(Raise(NotificationType.IpUsed, "IP assigned",
                        $"{ip.Address} assigned to {r.OwnerName ?? r.DeviceName ?? device.MacAddress}", now, ip.Address, device.MacAddress));
            }
            ip.LastSeen = now;
            return events;
        }
    }

    // --------------------------------------------------------------- queries

    public DashboardSnapshot GetDashboard()
    {
        lock (_gate)
        {
            var ips = _ipByAddr.Values;
            var stats = new DashboardStats(
                Total: ips.Count,
                Used: ips.Count(i => i.Status == IpStatus.Used),
                Available: ips.Count(i => i.Status == IpStatus.Available),
                Reserved: ips.Count(i => i.Status == IpStatus.Reserved),
                Conflicts: ips.Count(i => i.Status == IpStatus.Conflict),
                Online: _devByMac.Values.Count(d => d.HasInternetAccess),
                Devices: _devByMac.Count);

            var subnets = ips.GroupBy(i => i.Subnet ?? "—")
                .OrderBy(g => g.Min(i => i.IpNumeric))
                .Select(g => new ScopeSubnet(g.Key,
                    g.OrderBy(i => i.IpNumeric).Select(ToCell).ToList()))
                .ToList();

            return new DashboardSnapshot(stats, subnets);
        }
    }

    public PagedResult<IpRow> QueryIpRows(IpQuery q)
    {
        lock (_gate)
        {
            IEnumerable<IpAddress> rows = _ipByAddr.Values;

            if (!string.IsNullOrWhiteSpace(q.Status) &&
                Enum.TryParse<IpStatus>(q.Status, true, out var st))
                rows = rows.Where(i => i.Status == st);

            if (!string.IsNullOrWhiteSpace(q.Search))
            {
                var term = q.Search.Trim();
                var macTerm = MacHelper.Normalize(term);
                rows = rows.Where(i =>
                    i.Address.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (macTerm.Length > 0 && i.CurrentDevice != null &&
                     i.CurrentDevice.MacNormalized.Contains(macTerm)));
            }

            var ordered = rows.OrderBy(i => i.IpNumeric).ToList();
            int total = ordered.Count;
            int page = Math.Max(1, q.Page);
            int size = Math.Clamp(q.PageSize, 1, 500);

            var items = ordered.Skip((page - 1) * size).Take(size).Select(ToRow).ToList();
            return new PagedResult<IpRow>(items, total, page, size);
        }
    }

    public DeviceDetails? GetDeviceDetails(int id)
    {
        lock (_gate)
        {
            if (!_devById.TryGetValue(id, out var d)) return null;
            var active = _bindings.Where(b => b.DeviceId == id && b.IsActive)
                                  .OrderByDescending(b => b.LeaseStart).FirstOrDefault();

            var history = _bindings.Where(b => b.DeviceId == id)
                .OrderByDescending(b => b.LeaseStart)
                .Select(b => new BindingHistoryRow(_ipById[b.IpAddressId].Address, b.Source.ToString(), b.LeaseStart, b.LeaseEnd, b.IsActive))
                .Take(25).ToList();

            var activity = _activities.Where(a => a.DeviceId == id)
                .OrderByDescending(a => a.ActivityTime)
                .Select(a => new ActivityRow(a.ActivityTime, a.DestinationIp, a.BytesIn, a.BytesOut, a.Source))
                .Take(25).ToList();

            return new DeviceDetails(d.Id, d.MacAddress, d.Hostname, d.DeviceName, d.DeviceType,
                d.Department, d.Location, d.FirstSeen, d.LastSeen, d.HasInternetAccess, d.LastInternetActivity,
                active is null ? null : _ipById[active.IpAddressId].Address,
                d.OwnerName, d.OwnerEmail, d.OwnerPhone, d.Cpu, d.RamGb, d.StorageGb, d.OperatingSystem, d.AssetTag, d.Notes,
                history, activity);
        }
    }

    public IReadOnlyList<ConflictView> GetConflictViews(bool includeResolved)
    {
        lock (_gate)
        {
            return _conflicts
                .Where(c => includeResolved || !c.IsResolved)
                .OrderByDescending(c => c.DetectedAt)
                .Select(c => new ConflictView(
                    _ipById[c.IpAddressId].Address, c.DetectedAt, c.IsResolved,
                    c.Devices.Select(cd =>
                    {
                        var dev = _devById[cd.DeviceId];
                        return new ConflictDeviceView(dev.Id, dev.MacAddress, dev.Hostname, dev.DeviceType, dev.Location, dev.LastSeen);
                    }).ToList()))
                .ToList();
        }
    }

    public NotificationFeed GetNotifications(int take)
    {
        lock (_gate)
        {
            int unread = _notifications.Count(n => !n.IsRead);
            var items = _notifications.OrderByDescending(n => n.CreatedAt).Take(take)
                .Select(n => new NotificationDto(n.Id, n.Type.ToString(), n.Title, n.Message, n.RelatedIp, n.RelatedMac, n.CreatedAt, n.IsRead))
                .ToList();
            return new NotificationFeed(unread, items);
        }
    }

    public void MarkNotificationsRead()
    {
        lock (_gate) { foreach (var n in _notifications) n.IsRead = true; }
    }

    // --------------------------------------------------------------- helpers

    private IpAddress GetOrAddIp(string addr)
    {
        if (_ipByAddr.TryGetValue(addr, out var ip)) return ip;
        ip = new IpAddress { Id = ++_ipSeq, Address = addr, IpNumeric = IpHelper.ToNumeric(addr), FirstSeen = DateTime.UtcNow };
        _ipByAddr[addr] = ip; _ipById[ip.Id] = ip;
        return ip;
    }

    private Device GetOrAddDevice(string mac, NetworkObservation o, DateTime now)
    {
        if (_devByMac.TryGetValue(mac, out var d))
        {
            d.Hostname ??= o.Hostname;
            d.DeviceType ??= o.DeviceType;
            d.Department ??= o.Department;
            d.Location ??= o.Location;
            return d;
        }
        d = new Device
        {
            Id = ++_devSeq, MacNormalized = mac, MacAddress = MacHelper.ToDisplay(mac),
            Hostname = o.Hostname, DeviceName = o.Hostname, DeviceType = o.DeviceType,
            Department = o.Department, Location = o.Location, FirstSeen = now, LastSeen = now
        };
        _devByMac[mac] = d; _devById[d.Id] = d;
        return d;
    }

    private void AddBinding(IpAddress ip, Device device, BindingSource source, DateTime now, DateTime? leaseEnd)
    {
        var b = new IpBinding
        {
            Id = ++_bindSeq, IpAddressId = ip.Id, IpAddress = ip, DeviceId = device.Id, Device = device,
            Source = source, LeaseStart = now, LeaseEnd = leaseEnd, IsActive = true, DetectedAt = now
        };
        _bindings.Add(b);
        _missCount[b.Id] = 0;
    }

    private void Deactivate(IpBinding b, DateTime now)
    {
        b.IsActive = false;
        b.LeaseEnd ??= now;
        _missCount.Remove(b.Id);
    }

    private void RecomputeIpStatus(IpAddress ip, DateTime now, List<DomainEvent> events)
    {
        var active = _bindings.Where(b => b.IpAddressId == ip.Id && b.IsActive).ToList();
        var distinctDevices = active.Select(b => b.DeviceId).Distinct().ToList();

        if (distinctDevices.Count == 0)
        {
            if (ip.Status != IpStatus.Reserved) ip.Status = IpStatus.Available;
            ip.CurrentDeviceId = null; ip.CurrentDevice = null;
        }
        else if (distinctDevices.Count == 1)
        {
            ip.Status = IpStatus.Used;
            ip.CurrentDeviceId = distinctDevices[0];
            ip.CurrentDevice = _devById[distinctDevices[0]];
        }
        else
        {
            ip.Status = IpStatus.Conflict;
            ip.CurrentDeviceId = distinctDevices[0];
            ip.CurrentDevice = _devById[distinctDevices[0]];
        }
    }

    private DomainEvent Raise(NotificationType type, string title, string msg, DateTime at, string? ip, string? mac)
    {
        _notifications.Add(new Notification
        {
            Id = ++_notifSeq, Type = type, Title = title, Message = msg,
            RelatedIp = ip, RelatedMac = mac, CreatedAt = at, IsRead = false
        });
        return new DomainEvent(type, title, msg, ip, mac);
    }

    private ScopeCell ToCell(IpAddress i) => new(
        i.Address, IpHelper.LastOctet(i.Address), i.Status.ToString(),
        i.CurrentDevice?.HasInternetAccess ?? false,
        i.CurrentDevice?.MacAddress, i.CurrentDevice?.Hostname, i.CurrentDeviceId);

    private IpRow ToRow(IpAddress i) => new(
        i.Address, i.IpNumeric, i.Status.ToString(),
        i.CurrentDevice?.MacAddress, i.CurrentDevice?.Hostname, i.CurrentDevice?.DeviceName,
        i.CurrentDevice?.DeviceType, i.CurrentDevice?.Department, i.CurrentDevice?.Location,
        i.CurrentDevice?.HasInternetAccess ?? false, i.LastSeen, i.CurrentDevice?.LastInternetActivity, i.CurrentDeviceId,
        i.CurrentDevice?.OwnerName);
}
