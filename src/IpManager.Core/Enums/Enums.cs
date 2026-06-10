namespace IpManager.Core.Enums;

/// <summary>Lifecycle state of a single IP address slot on the network.</summary>
public enum IpStatus
{
    Available = 0,
    Used = 1,
    Reserved = 2,
    Conflict = 3
}

/// <summary>Where the knowledge of an IP-to-device binding came from.</summary>
public enum BindingSource
{
    Dhcp = 0,
    Arp = 1,
    Snmp = 2,
    Static = 3,
    Manual = 4,
    Simulated = 5
}

/// <summary>The four events the system raises notifications for.</summary>
public enum NotificationType
{
    IpUsed = 0,
    IpFreed = 1,
    ConflictDetected = 2,
    InternetAccessStarted = 3
}

/// <summary>Demo (self-driving sample data) or Live (real network collectors).</summary>
public enum RunMode
{
    Demo = 0,
    Live = 1
}
