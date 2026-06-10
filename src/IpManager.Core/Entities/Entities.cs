using IpManager.Core.Enums;

namespace IpManager.Core.Entities;

/// <summary>One row per IP address in a managed range. Holds status and the numeric sort key.</summary>
public class IpAddress
{
    public int Id { get; set; }
    public string Address { get; set; } = "";    // "192.168.1.10" — used for partial search
    public long IpNumeric { get; set; }           // 3232235786   — used for correct numeric sort
    public string? Subnet { get; set; }           // "192.168.1.0/24"
    public IpStatus Status { get; set; } = IpStatus.Available;
    public string? Notes { get; set; }
    public DateTime? FirstSeen { get; set; }
    public DateTime? LastSeen { get; set; }

    public int? CurrentDeviceId { get; set; }
    public Device? CurrentDevice { get; set; }

    public List<IpBinding> Bindings { get; set; } = new();
}

/// <summary>One row per physical device, keyed by its MAC address.</summary>
public class Device
{
    public int Id { get; set; }
    public string MacAddress { get; set; } = "";   // display form "AA:BB:CC:11:22:33"
    public string MacNormalized { get; set; } = ""; // "AABBCC112233" — search + dedupe key
    public string? Hostname { get; set; }
    public string? DeviceName { get; set; }
    public string? DeviceType { get; set; }         // PC, Phone, Printer, AP, Server, IoT...
    public string? Department { get; set; }
    public string? Location { get; set; }           // building / switch port
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public bool HasInternetAccess { get; set; }
    public DateTime? LastInternetActivity { get; set; }

    // --- asset / inventory details (manually entered) ---
    public string? OwnerName { get; set; }          // e.g. "Eng. Ahmed Shaker"
    public string? OwnerEmail { get; set; }
    public string? OwnerPhone { get; set; }
    public string? Cpu { get; set; }                // e.g. "Intel Core i7-12700"
    public int? RamGb { get; set; }                 // e.g. 16
    public int? StorageGb { get; set; }             // e.g. 512
    public string? OperatingSystem { get; set; }    // e.g. "Windows 11 Pro"
    public string? AssetTag { get; set; }           // company asset number
    public string? Notes { get; set; }
    public bool IsManaged { get; set; }             // true once details were entered by hand

    public List<IpBinding> Bindings { get; set; } = new();
}

/// <summary>History of "device D held IP X from .. to ..". Active rows = current network state.</summary>
public class IpBinding
{
    public int Id { get; set; }
    public int IpAddressId { get; set; }
    public IpAddress? IpAddress { get; set; }
    public int DeviceId { get; set; }
    public Device? Device { get; set; }

    public BindingSource Source { get; set; }
    public DateTime LeaseStart { get; set; }
    public DateTime? LeaseEnd { get; set; }   // null = still active / no expiry (static)
    public bool IsActive { get; set; }
    public DateTime DetectedAt { get; set; }

    /// <summary>Consecutive scan cycles this active binding was NOT observed. Used by the
    /// EF store to decide when to free an address. The in-memory store tracks this separately.</summary>
    public int MissedScans { get; set; }
}

/// <summary>One open conflict per IP that is claimed by more than one device.</summary>
public class Conflict
{
    public int Id { get; set; }
    public int IpAddressId { get; set; }
    public IpAddress? IpAddress { get; set; }
    public DateTime DetectedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public bool IsResolved { get; set; }
    public List<ConflictDevice> Devices { get; set; } = new();
}

public class ConflictDevice
{
    public int ConflictId { get; set; }
    public Conflict? Conflict { get; set; }
    public int DeviceId { get; set; }
    public Device? Device { get; set; }
}

/// <summary>An internet-usage event attributed to a device + IP at a point in time.</summary>
public class InternetActivity
{
    public int Id { get; set; }
    public int DeviceId { get; set; }
    public Device? Device { get; set; }
    public int IpAddressId { get; set; }
    public DateTime ActivityTime { get; set; }
    public string? DestinationIp { get; set; }
    public long BytesIn { get; set; }
    public long BytesOut { get; set; }
    public string Source { get; set; } = "";   // "NetFlow" / "Firewall" / "Proxy"
}

public class Notification
{
    public int Id { get; set; }
    public NotificationType Type { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string? RelatedIp { get; set; }
    public string? RelatedMac { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
}

public class ScanRun
{
    public int Id { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Source { get; set; } = "";
    public int DevicesFound { get; set; }
    public string? Notes { get; set; }
}
