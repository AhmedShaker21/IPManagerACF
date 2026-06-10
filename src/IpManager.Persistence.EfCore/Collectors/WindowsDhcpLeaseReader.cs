using System.Text.Json;
using IpManager.Core.Abstractions;
using IpManager.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IpManager.Persistence.EfCore.Collectors;

/// <summary>
/// Reads active leases from a Windows DHCP server by invoking a PowerShell script
/// (tools/get-leases.ps1) that calls Get-DhcpServerv4Lease and emits JSON. DHCP is the richest
/// source: it gives IP + MAC + hostname + lease expiry in one shot, and — crucially — the DHCP
/// server sees every scope it serves, including subnets this host is not attached to.
/// </summary>
public sealed class WindowsDhcpLeaseReader : IDhcpLeaseReader
{
    private readonly DhcpOptions _opts;
    private readonly ILogger<WindowsDhcpLeaseReader> _log;

    public WindowsDhcpLeaseReader(IOptions<NetworkOptions> opts, ILogger<WindowsDhcpLeaseReader> log)
    {
        _opts = opts.Value.Dhcp;
        _log = log;
    }

    public async Task<IReadOnlyList<DhcpLease>> ReadAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.ScriptPath))
            return Array.Empty<DhcpLease>();

        try
        {
            var args = $"-NoProfile -ExecutionPolicy Bypass -File \"{_opts.ScriptPath}\"";
            if (!string.IsNullOrWhiteSpace(_opts.ServerHost))
                args += $" -ComputerName \"{_opts.ServerHost}\"";

            var json = await ProcessRunner.RunAsync("powershell.exe", args, ct);
            if (string.IsNullOrWhiteSpace(json)) return Array.Empty<DhcpLease>();

            var raw = JsonSerializer.Deserialize<List<LeaseJson>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new();

            return raw.Where(r => !string.IsNullOrWhiteSpace(r.IPAddress) && !string.IsNullOrWhiteSpace(r.ClientId))
                .Select(r => new DhcpLease(
                    r.IPAddress!,
                    r.ClientId!,
                    string.IsNullOrWhiteSpace(r.HostName) ? null : r.HostName,
                    Active: r.AddressState?.StartsWith("Active", StringComparison.OrdinalIgnoreCase) ?? true,
                    ExpiresAt: r.LeaseExpiryTime))
                .ToList();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "DHCP lease read failed (script {Path}).", _opts.ScriptPath);
            return Array.Empty<DhcpLease>();
        }
    }

    private sealed class LeaseJson
    {
        public string? IPAddress { get; set; }
        public string? ClientId { get; set; }      // MAC
        public string? HostName { get; set; }
        public string? AddressState { get; set; }  // Active / ActiveReservation / ...
        public DateTime? LeaseExpiryTime { get; set; }
    }
}
