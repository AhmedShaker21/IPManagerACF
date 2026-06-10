using IpManager.Core.Enums;

namespace IpManager.Core.Options;

public class NetworkOptions
{
    public const string SectionName = "Network";

    /// <summary>Demo = self-driving sample data. Live = real collectors (DHCP/ARP/SNMP/gateway).</summary>
    public RunMode Mode { get; set; } = RunMode.Demo;

    /// <summary>Managed ranges, e.g. ["192.168.1.0/24", "192.168.5.0/24"].</summary>
    public string[] Subnets { get; set; } = { "192.168.1.0/24" };

    public int ScanIntervalSeconds { get; set; } = 60;
    public int DhcpIntervalSeconds { get; set; } = 120;

    /// <summary>How many missed scans before a used IP is released back to Available.</summary>
    public int MissedScansBeforeFree { get; set; } = 3;

    public DemoOptions Demo { get; set; } = new();
    public SnmpOptions Snmp { get; set; } = new();
    public SyslogOptions Syslog { get; set; } = new();
    public DhcpOptions Dhcp { get; set; } = new();
}

public class DemoOptions
{
    public int TickSeconds { get; set; } = 4;
    public int SeedDevices { get; set; } = 40;
    public int Seed { get; set; } = 1337;
}

public class SnmpOptions
{
    public bool Enabled { get; set; }
    public string[] RouterHosts { get; set; } = Array.Empty<string>();
    public string Community { get; set; } = "public";
    public int Port { get; set; } = 161;
    public int TimeoutMs { get; set; } = 3000;
}

public class SyslogOptions
{
    public bool Enabled { get; set; }
    public int UdpPort { get; set; } = 5514;
    /// <summary>Internal LAN ranges; a flow whose destination is NOT inside these = internet.</summary>
    public string[] InternalRanges { get; set; } = { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16" };
}

public class DhcpOptions
{
    public bool Enabled { get; set; }
    public string ScriptPath { get; set; } = "";   // path to get-leases.ps1 on the collector
    public string? ServerHost { get; set; }        // optional remote DHCP server
}
