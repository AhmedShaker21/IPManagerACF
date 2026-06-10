using IpManager.Core.Dtos;

namespace IpManager.Core.Abstractions;

// ----- Storage + domain logic seam ------------------------------------------
// Two implementations exist: InMemoryNetworkStore (default, runs anywhere) and
// EfNetworkStore (SQL Server, in IpManager.Persistence.EfCore). Services depend
// only on this interface, so swapping the database changes nothing above it.

public interface INetworkStore
{
    /// <summary>Create the IP rows for the managed subnets if they don't exist yet.</summary>
    void EnsureSeeded(IEnumerable<string> subnets);

    // --- domain mutations (each returns the events the UI should hear about) ---
    IReadOnlyList<DomainEvent> ApplyObservation(NetworkObservation observation);
    IReadOnlyList<DomainEvent> ExpireStale(IReadOnlySet<string> seenIps, DateTime now, int missedScansBeforeFree);
    IReadOnlyList<DomainEvent> ScanConflicts();
    IReadOnlyList<DomainEvent> ApplyInternetEvent(InternetEvent ev);

    /// <summary>Manually assign an IP to a person/machine and record asset details.</summary>
    IReadOnlyList<DomainEvent> AssignIp(AssignIpRequest request);

    // --- queries (return view-ready DTOs) ---
    DashboardSnapshot GetDashboard();
    PagedResult<IpRow> QueryIpRows(IpQuery query);
    DeviceDetails? GetDeviceDetails(int id);
    IReadOnlyList<ConflictView> GetConflictViews(bool includeResolved);
    NotificationFeed GetNotifications(int take);
    void MarkNotificationsRead();
}

/// <summary>Pushes live updates to connected dashboards. Implemented over SignalR in the web app.</summary>
public interface INotificationPublisher
{
    Task PublishNotificationAsync(NotificationDto notification);
    Task PublishStateChangedAsync();
}

// ----- Live-mode collectors --------------------------------------------------

public record HostEntry(string Ip, string Mac, string? Hostname);
public record DhcpLease(string Ip, string Mac, string? Hostname, bool Active, DateTime? ExpiresAt);

/// <summary>ICMP sweep a subnet to learn which addresses respond.</summary>
public interface INetworkScanner
{
    Task<IReadOnlyList<string>> SweepAsync(string cidr, CancellationToken ct);
}

/// <summary>Read the local ARP cache (same-subnet IP -> MAC).</summary>
public interface IArpReader
{
    Task<IReadOnlyList<HostEntry>> ReadAsync(CancellationToken ct);
}

/// <summary>Read DHCP leases (rich: IP, MAC, hostname, lease time, state).</summary>
public interface IDhcpLeaseReader
{
    Task<IReadOnlyList<DhcpLease>> ReadAsync(CancellationToken ct);
}

/// <summary>Walk a router/switch ARP table over SNMP — sees MACs ACROSS subnets/VLANs.</summary>
public interface ISnmpArpReader
{
    Task<IReadOnlyList<HostEntry>> ReadAsync(CancellationToken ct);
}

/// <summary>Stream internet-usage events from the gateway (proxy/firewall/NetFlow).</summary>
public interface IInternetActivityReader
{
    IAsyncEnumerable<InternetEvent> ReadAsync(CancellationToken ct);
}

/// <summary>Application service that turns store events into persisted + pushed notifications.</summary>
public interface INetworkService
{
    void EnsureSeeded();
    Task ApplyObservationAsync(NetworkObservation observation);
    Task RunScanCycleAsync(IReadOnlyList<NetworkObservation> observations, IReadOnlySet<string> seenIps);
    Task ApplyInternetEventAsync(InternetEvent ev);
    Task DetectConflictsAsync();
}
