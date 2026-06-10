using IpManager.Core.Dtos;

namespace IpManager.Web.ViewModels;

public class AssignViewModel
{
    public string Ip { get; set; } = "";
    public string? Mac { get; set; }
    public string? DeviceName { get; set; }
    public string? Hostname { get; set; }
    public string? DeviceType { get; set; }
    public string? Department { get; set; }
    public string? Location { get; set; }

    public string? OwnerName { get; set; }
    public string? OwnerEmail { get; set; }
    public string? OwnerPhone { get; set; }

    public string? Cpu { get; set; }
    public int? RamGb { get; set; }
    public int? StorageGb { get; set; }
    public string? OperatingSystem { get; set; }
    public string? AssetTag { get; set; }
    public string? Notes { get; set; }

    public AssignIpRequest ToRequest() => new(
        Ip?.Trim() ?? "", Mac, Hostname, DeviceName, DeviceType, Department, Location,
        OwnerName, OwnerEmail, OwnerPhone, Cpu, RamGb, StorageGb, OperatingSystem, AssetTag, Notes);
}
