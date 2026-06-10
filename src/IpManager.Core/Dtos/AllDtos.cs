using IpManager.Core.Enums;

namespace IpManager.Core.Dtos;

// ----- Inputs from collectors / simulator -----------------------------------

/// <summary>A single observation of "this IP is held by this MAC" from any source.</summary>
public record NetworkObservation(
    string Ip,
    string Mac,
    string? Hostname,
    string? DeviceType,
    string? Department,
    string? Location,
    BindingSource Source,
    DateTime? LeaseEnd,
    DateTime At);

/// <summary>An internet-usage record handed over by a gateway source (proxy/firewall/flow).</summary>
public record InternetEvent(
    string InternalIp,
    DateTime At,
    string? DestinationIp,
    long BytesIn,
    long BytesOut,
    string Source);

/// <summary>Something happened that the UI should be told about.</summary>
public record DomainEvent(NotificationType Type, string Title, string Message, string? Ip, string? Mac);

// ----- Query inputs ----------------------------------------------------------

public record IpQuery(string? Search, string? Status, int Page, int PageSize);

// ----- View-ready outputs ----------------------------------------------------

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling(Total / (double)PageSize);
}

public record IpRow(
    string Ip,
    long IpNumeric,
    string Status,
    string? Mac,
    string? Hostname,
    string? DeviceName,
    string? DeviceType,
    string? Department,
    string? Location,
    bool HasInternet,
    DateTime? LastSeen,
    DateTime? LastInternet,
    int? DeviceId,
    string? OwnerName);

public record DashboardStats(
    int Total, int Used, int Available, int Reserved, int Conflicts, int Online, int Devices);

public record ScopeCell(string Ip, int LastOctet, string Status, bool Online, string? Mac, string? Hostname, int? DeviceId);

public record ScopeSubnet(string Label, IReadOnlyList<ScopeCell> Cells);

public record DashboardSnapshot(DashboardStats Stats, IReadOnlyList<ScopeSubnet> Subnets);

public record BindingHistoryRow(string Ip, string Source, DateTime Start, DateTime? End, bool Active);

public record ActivityRow(DateTime At, string? Destination, long BytesIn, long BytesOut, string Source);

public record DeviceDetails(
    int Id,
    string Mac,
    string? Hostname,
    string? DeviceName,
    string? DeviceType,
    string? Department,
    string? Location,
    DateTime FirstSeen,
    DateTime LastSeen,
    bool HasInternet,
    DateTime? LastInternet,
    string? CurrentIp,
    string? OwnerName,
    string? OwnerEmail,
    string? OwnerPhone,
    string? Cpu,
    int? RamGb,
    int? StorageGb,
    string? OperatingSystem,
    string? AssetTag,
    string? Notes,
    IReadOnlyList<BindingHistoryRow> History,
    IReadOnlyList<ActivityRow> Activity);

/// <summary>Manual assignment of an IP to a person + machine, with hardware/asset details.</summary>
public record AssignIpRequest(
    string Ip,
    string? Mac,
    string? Hostname,
    string? DeviceName,
    string? DeviceType,
    string? Department,
    string? Location,
    string? OwnerName,
    string? OwnerEmail,
    string? OwnerPhone,
    string? Cpu,
    int? RamGb,
    int? StorageGb,
    string? OperatingSystem,
    string? AssetTag,
    string? Notes);

public record ConflictDeviceView(int DeviceId, string Mac, string? Hostname, string? DeviceType, string? Location, DateTime LastSeen);

public record ConflictView(string Ip, DateTime DetectedAt, bool Resolved, IReadOnlyList<ConflictDeviceView> Devices);

public record NotificationDto(int Id, string Type, string Title, string Message, string? Ip, string? Mac, DateTime CreatedAt, bool IsRead);

public record NotificationFeed(int Unread, IReadOnlyList<NotificationDto> Items);
