using IpManager.Core.Abstractions;
using IpManager.Core.Dtos;
using IpManager.Core.Entities;
using IpManager.Core.Enums;
using IpManager.Core.Util;
using IpManager.Persistence.EfCore.Data;
using Microsoft.EntityFrameworkCore;

namespace IpManager.Persistence.EfCore.Services;

/// <summary>
/// SQL Server implementation of <see cref="INetworkStore"/>. Mirrors InMemoryNetworkStore exactly.
/// Uses <see cref="IDbContextFactory{TContext}"/> so it can be a thread-safe singleton: every call
/// opens and disposes its own short-lived context.
/// </summary>
public sealed class EfNetworkStore : INetworkStore
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly object _gate = new(); // serialize writers; reads are independent contexts

    public EfNetworkStore(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    // ---------------------------------------------------------------- seeding

    public void EnsureSeeded(IEnumerable<string> subnets)
    {
        lock (_gate)
        {
            using var db = _factory.CreateDbContext();
            db.Database.Migrate(); // apply pending migrations on first run

            var existing = db.IpAddresses.Select(i => i.Address).ToHashSet();
            foreach (var cidr in subnets)
            foreach (var addr in IpHelper.EnumerateHosts(cidr))
            {
                if (!existing.Add(addr)) continue;
                db.IpAddresses.Add(new IpAddress
                {
                    Address = addr,
                    IpNumeric = IpHelper.ToNumeric(addr),
                    Subnet = cidr,
                    Status = IpStatus.Available
                });
            }
            db.SaveChanges();
        }
    }

    // ------------------------------------------------------------- mutations

    public IReadOnlyList<DomainEvent> ApplyObservation(NetworkObservation o)
    {
        lock (_gate)
        {
            using var db = _factory.CreateDbContext();
            var events = new List<DomainEvent>();
            var now = o.At;

            var ip = GetOrAddIp(db, o.Ip);
            var mac = MacHelper.Normalize(o.Mac);
            var device = GetOrAddDevice(db, mac, o, now);
            db.SaveChanges(); // ensure ids assigned

            foreach (var b in db.IpBindings.Where(b => b.DeviceId == device.Id && b.IsActive && b.IpAddressId != ip.Id).ToList())
                Deactivate(b, now);

            var existing = db.IpBindings.FirstOrDefault(b => b.IpAddressId == ip.Id && b.DeviceId == device.Id && b.IsActive);
            if (existing is null)
            {
                AddBinding(db, ip, device, o.Source, now, o.LeaseEnd);
                db.SaveChanges();

                bool wasAvailable = ip.Status == IpStatus.Available;
                RecomputeIpStatus(db, ip, now);
                if (wasAvailable && ip.Status == IpStatus.Used)
                    events.Add(Raise(db, NotificationType.IpUsed, "IP now in use",
                        $"{ip.Address} is now used by {device.MacAddress}" +
                        (device.Hostname is { Length: > 0 } ? $" ({device.Hostname})" : ""), now, ip.Address, device.MacAddress));
            }
            else
            {
                existing.LeaseEnd = o.LeaseEnd;
                existing.MissedScans = 0;
            }

            device.LastSeen = now;
            ip.LastSeen = now;
            db.SaveChanges();
            return events;
        }
    }

    public IReadOnlyList<DomainEvent> ExpireStale(IReadOnlySet<string> seenIps, DateTime now, int missedScansBeforeFree)
    {
        lock (_gate)
        {
            using var db = _factory.CreateDbContext();
            var events = new List<DomainEvent>();

            var active = db.IpBindings.Include(b => b.IpAddress).Where(b => b.IsActive).ToList();
            foreach (var b in active)
            {
                var addr = b.IpAddress!.Address;
                bool leaseExpired = b.LeaseEnd is null || b.LeaseEnd < now;

                if (seenIps.Contains(addr)) { b.MissedScans = 0; continue; }

                b.MissedScans += 1;
                if (b.MissedScans < missedScansBeforeFree || !leaseExpired) continue;

                Deactivate(b, now);
                db.SaveChanges();
                RecomputeIpStatus(db, b.IpAddress!, now);
                if (b.IpAddress!.Status == IpStatus.Available)
                    events.Add(Raise(db, NotificationType.IpFreed, "IP freed",
                        $"{b.IpAddress.Address} is now available.", now, b.IpAddress.Address, null));
            }
            db.SaveChanges();
            return events;
        }
    }

    public IReadOnlyList<DomainEvent> ScanConflicts()
    {
        lock (_gate)
        {
            using var db = _factory.CreateDbContext();
            var events = new List<DomainEvent>();
            var now = DateTime.UtcNow;

            var activeByIp = db.IpBindings.Where(b => b.IsActive)
                .Select(b => new { b.IpAddressId, b.DeviceId })
                .ToList()
                .GroupBy(x => x.IpAddressId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.DeviceId).Distinct().ToList());

            foreach (var (ipId, deviceIds) in activeByIp.Where(kv => kv.Value.Count > 1))
            {
                var ip = db.IpAddresses.First(i => i.Id == ipId);
                ip.Status = IpStatus.Conflict;

                bool open = db.Conflicts.Any(c => c.IpAddressId == ipId && !c.IsResolved);
                if (!open)
                {
                    var conflict = new Conflict { IpAddressId = ipId, DetectedAt = now };
                    foreach (var did in deviceIds)
                        conflict.Devices.Add(new ConflictDevice { DeviceId = did });
                    db.Conflicts.Add(conflict);
                    events.Add(Raise(db, NotificationType.ConflictDetected, "IP conflict detected",
                        $"{ip.Address} is claimed by {deviceIds.Count} devices.", now, ip.Address, null));
                }
            }

            foreach (var c in db.Conflicts.Where(c => !c.IsResolved).ToList())
            {
                bool still = activeByIp.TryGetValue(c.IpAddressId, out var ids) && ids.Count > 1;
                if (still) continue;
                c.IsResolved = true;
                c.ResolvedAt = now;
                var ip = db.IpAddresses.First(i => i.Id == c.IpAddressId);
                RecomputeIpStatus(db, ip, now);
            }
            db.SaveChanges();
            return events;
        }
    }

    public IReadOnlyList<DomainEvent> ApplyInternetEvent(InternetEvent e)
    {
        lock (_gate)
        {
            using var db = _factory.CreateDbContext();
            var events = new List<DomainEvent>();

            var ip = db.IpAddresses.FirstOrDefault(i => i.Address == e.InternalIp);
            if (ip is null) return events;

            var binding = db.IpBindings
                .Where(b => b.IpAddressId == ip.Id && b.LeaseStart <= e.At && (b.LeaseEnd == null || b.LeaseEnd >= e.At))
                .OrderByDescending(b => b.LeaseStart).FirstOrDefault()
                ?? db.IpBindings.Where(b => b.IpAddressId == ip.Id && b.IsActive)
                    .OrderByDescending(b => b.LeaseStart).FirstOrDefault();
            if (binding is null) return events;

            db.InternetActivities.Add(new InternetActivity
            {
                DeviceId = binding.DeviceId, IpAddressId = ip.Id, ActivityTime = e.At,
                DestinationIp = e.DestinationIp, BytesIn = e.BytesIn, BytesOut = e.BytesOut, Source = e.Source
            });

            var device = db.Devices.First(d => d.Id == binding.DeviceId);
            bool firstTime = !device.HasInternetAccess;
            device.HasInternetAccess = true;
            device.LastInternetActivity = e.At;
            if (firstTime)
                events.Add(Raise(db, NotificationType.InternetAccessStarted, "Device went online",
                    $"{device.MacAddress} ({e.InternalIp}) started using the internet.", e.At, e.InternalIp, device.MacAddress));

            db.SaveChanges();
            return events;
        }
    }

    // --------------------------------------------------------------- queries

    public IReadOnlyList<DomainEvent> AssignIp(AssignIpRequest r)
    {
        lock (_gate)
        {
            using var db = _factory.CreateDbContext();
            var events = new List<DomainEvent>();
            var now = DateTime.UtcNow;

            var ip = GetOrAddIp(db, r.Ip);

            var mac = string.IsNullOrWhiteSpace(r.Mac)
                ? $"MANUAL{IpHelper.ToNumeric(r.Ip):X}"
                : MacHelper.Normalize(r.Mac);

            var device = db.Devices.FirstOrDefault(d => d.MacNormalized == mac);
            if (device is null)
            {
                device = new Device
                {
                    MacNormalized = mac,
                    MacAddress = string.IsNullOrWhiteSpace(r.Mac) ? "(manual)" : MacHelper.ToDisplay(mac),
                    FirstSeen = now, LastSeen = now
                };
                db.Devices.Add(device);
            }

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
            db.SaveChanges();

            foreach (var b in db.IpBindings.Where(b => b.DeviceId == device.Id && b.IsActive && b.IpAddressId != ip.Id).ToList())
                Deactivate(b, now);

            var existing = db.IpBindings.FirstOrDefault(b => b.IpAddressId == ip.Id && b.DeviceId == device.Id && b.IsActive);
            if (existing is null)
            {
                AddBinding(db, ip, device, BindingSource.Manual, now, null);
                db.SaveChanges();
                bool wasAvailable = ip.Status == IpStatus.Available;
                RecomputeIpStatus(db, ip, now);
                if (wasAvailable && ip.Status == IpStatus.Used)
                    events.Add(Raise(db, NotificationType.IpUsed, "IP assigned",
                        $"{ip.Address} assigned to {r.OwnerName ?? r.DeviceName ?? device.MacAddress}", now, ip.Address, device.MacAddress));
            }
            ip.LastSeen = now;
            db.SaveChanges();
            return events;
        }
    }


    public DashboardSnapshot GetDashboard()
    {
        using var db = _factory.CreateDbContext();
        var ips = db.IpAddresses.Include(i => i.CurrentDevice).AsNoTracking().ToList();

        var stats = new DashboardStats(
            Total: ips.Count,
            Used: ips.Count(i => i.Status == IpStatus.Used),
            Available: ips.Count(i => i.Status == IpStatus.Available),
            Reserved: ips.Count(i => i.Status == IpStatus.Reserved),
            Conflicts: ips.Count(i => i.Status == IpStatus.Conflict),
            Online: db.Devices.Count(d => d.HasInternetAccess),
            Devices: db.Devices.Count());

        var subnets = ips.GroupBy(i => i.Subnet ?? "—")
            .OrderBy(g => g.Min(i => i.IpNumeric))
            .Select(g => new ScopeSubnet(g.Key, g.OrderBy(i => i.IpNumeric).Select(ToCell).ToList()))
            .ToList();

        return new DashboardSnapshot(stats, subnets);
    }

    public PagedResult<IpRow> QueryIpRows(IpQuery q)
    {
        using var db = _factory.CreateDbContext();
        IQueryable<IpAddress> rows = db.IpAddresses.Include(i => i.CurrentDevice).AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q.Status) && Enum.TryParse<IpStatus>(q.Status, true, out var st))
            rows = rows.Where(i => i.Status == st);

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim();
            var macTerm = MacHelper.Normalize(term);
            rows = rows.Where(i =>
                EF.Functions.Like(i.Address, $"%{term}%") ||
                (macTerm.Length > 0 && i.CurrentDevice != null && i.CurrentDevice.MacNormalized.Contains(macTerm)));
        }

        rows = rows.OrderBy(i => i.IpNumeric);
        int total = rows.Count();
        int page = Math.Max(1, q.Page);
        int size = Math.Clamp(q.PageSize, 1, 500);

        var items = rows.Skip((page - 1) * size).Take(size).ToList().Select(ToRow).ToList();
        return new PagedResult<IpRow>(items, total, page, size);
    }

    public DeviceDetails? GetDeviceDetails(int id)
    {
        using var db = _factory.CreateDbContext();
        var d = db.Devices.AsNoTracking().FirstOrDefault(x => x.Id == id);
        if (d is null) return null;

        var bindings = db.IpBindings.Include(b => b.IpAddress).AsNoTracking()
            .Where(b => b.DeviceId == id).OrderByDescending(b => b.LeaseStart).Take(25).ToList();

        var active = bindings.FirstOrDefault(b => b.IsActive);
        var history = bindings.Select(b => new BindingHistoryRow(b.IpAddress!.Address, b.Source.ToString(), b.LeaseStart, b.LeaseEnd, b.IsActive)).ToList();

        var activity = db.InternetActivities.AsNoTracking()
            .Where(a => a.DeviceId == id).OrderByDescending(a => a.ActivityTime).Take(25)
            .Select(a => new ActivityRow(a.ActivityTime, a.DestinationIp, a.BytesIn, a.BytesOut, a.Source)).ToList();

        return new DeviceDetails(d.Id, d.MacAddress, d.Hostname, d.DeviceName, d.DeviceType,
            d.Department, d.Location, d.FirstSeen, d.LastSeen, d.HasInternetAccess, d.LastInternetActivity,
            active?.IpAddress?.Address,
            d.OwnerName, d.OwnerEmail, d.OwnerPhone, d.Cpu, d.RamGb, d.StorageGb, d.OperatingSystem, d.AssetTag, d.Notes,
            history, activity);
    }

    public IReadOnlyList<ConflictView> GetConflictViews(bool includeResolved)
    {
        using var db = _factory.CreateDbContext();
        var conflicts = db.Conflicts.Include(c => c.IpAddress)
            .Include(c => c.Devices).ThenInclude(cd => cd.Device).AsNoTracking()
            .Where(c => includeResolved || !c.IsResolved)
            .OrderByDescending(c => c.DetectedAt).ToList();

        return conflicts.Select(c => new ConflictView(
            c.IpAddress!.Address, c.DetectedAt, c.IsResolved,
            c.Devices.Select(cd => new ConflictDeviceView(
                cd.Device!.Id, cd.Device.MacAddress, cd.Device.Hostname, cd.Device.DeviceType, cd.Device.Location, cd.Device.LastSeen)).ToList())).ToList();
    }

    public NotificationFeed GetNotifications(int take)
    {
        using var db = _factory.CreateDbContext();
        int unread = db.Notifications.Count(n => !n.IsRead);
        var items = db.Notifications.AsNoTracking().OrderByDescending(n => n.CreatedAt).Take(take)
            .ToList()
            .Select(n => new NotificationDto(n.Id, n.Type.ToString(), n.Title, n.Message, n.RelatedIp, n.RelatedMac, n.CreatedAt, n.IsRead))
            .ToList();
        return new NotificationFeed(unread, items);
    }

    public void MarkNotificationsRead()
    {
        lock (_gate)
        {
            using var db = _factory.CreateDbContext();
            db.Notifications.Where(n => !n.IsRead).ExecuteUpdate(s => s.SetProperty(n => n.IsRead, true));
        }
    }

    // --------------------------------------------------------------- helpers

    private static IpAddress GetOrAddIp(AppDbContext db, string addr)
    {
        var ip = db.IpAddresses.FirstOrDefault(i => i.Address == addr);
        if (ip is not null) return ip;
        ip = new IpAddress { Address = addr, IpNumeric = IpHelper.ToNumeric(addr), FirstSeen = DateTime.UtcNow };
        db.IpAddresses.Add(ip);
        return ip;
    }

    private static Device GetOrAddDevice(AppDbContext db, string mac, NetworkObservation o, DateTime now)
    {
        var d = db.Devices.FirstOrDefault(x => x.MacNormalized == mac);
        if (d is not null)
        {
            d.Hostname ??= o.Hostname;
            d.DeviceType ??= o.DeviceType;
            d.Department ??= o.Department;
            d.Location ??= o.Location;
            return d;
        }
        d = new Device
        {
            MacNormalized = mac, MacAddress = MacHelper.ToDisplay(mac),
            Hostname = o.Hostname, DeviceName = o.Hostname, DeviceType = o.DeviceType,
            Department = o.Department, Location = o.Location, FirstSeen = now, LastSeen = now
        };
        db.Devices.Add(d);
        return d;
    }

    private static void AddBinding(AppDbContext db, IpAddress ip, Device device, BindingSource source, DateTime now, DateTime? leaseEnd)
        => db.IpBindings.Add(new IpBinding
        {
            IpAddressId = ip.Id, DeviceId = device.Id, Source = source,
            LeaseStart = now, LeaseEnd = leaseEnd, IsActive = true, DetectedAt = now, MissedScans = 0
        });

    private static void Deactivate(IpBinding b, DateTime now)
    {
        b.IsActive = false;
        b.LeaseEnd ??= now;
    }

    private static void RecomputeIpStatus(AppDbContext db, IpAddress ip, DateTime now)
    {
        var distinct = db.IpBindings.Where(b => b.IpAddressId == ip.Id && b.IsActive)
            .Select(b => b.DeviceId).Distinct().ToList();

        if (distinct.Count == 0)
        {
            if (ip.Status != IpStatus.Reserved) ip.Status = IpStatus.Available;
            ip.CurrentDeviceId = null;
        }
        else
        {
            ip.Status = distinct.Count == 1 ? IpStatus.Used : IpStatus.Conflict;
            ip.CurrentDeviceId = distinct[0];
        }
    }

    private static DomainEvent Raise(AppDbContext db, NotificationType type, string title, string msg, DateTime at, string? ip, string? mac)
    {
        db.Notifications.Add(new Notification { Type = type, Title = title, Message = msg, RelatedIp = ip, RelatedMac = mac, CreatedAt = at });
        return new DomainEvent(type, title, msg, ip, mac);
    }

    private static ScopeCell ToCell(IpAddress i) => new(
        i.Address, IpHelper.LastOctet(i.Address), i.Status.ToString(),
        i.CurrentDevice?.HasInternetAccess ?? false, i.CurrentDevice?.MacAddress, i.CurrentDevice?.Hostname, i.CurrentDeviceId);

    private static IpRow ToRow(IpAddress i) => new(
        i.Address, i.IpNumeric, i.Status.ToString(),
        i.CurrentDevice?.MacAddress, i.CurrentDevice?.Hostname, i.CurrentDevice?.DeviceName,
        i.CurrentDevice?.DeviceType, i.CurrentDevice?.Department, i.CurrentDevice?.Location,
        i.CurrentDevice?.HasInternetAccess ?? false, i.LastSeen, i.CurrentDevice?.LastInternetActivity, i.CurrentDeviceId,
        i.CurrentDevice?.OwnerName);
}
