<#
    get-leases.ps1
    Emits all active IPv4 DHCP leases as JSON for the IP Manager DHCP collector.

    Run on the DHCP server, or pass -ComputerName to query a remote server
    (requires RSAT DhcpServer module + permissions).

    Output: JSON array of { IPAddress, ClientId, HostName, AddressState, LeaseExpiryTime }
    ClientId is the MAC address.
#>
param(
    [string]$ComputerName = $env:COMPUTERNAME
)

$ErrorActionPreference = "Stop"
Import-Module DhcpServer -ErrorAction SilentlyContinue

$leases = foreach ($scope in Get-DhcpServerv4Scope -ComputerName $ComputerName) {
    Get-DhcpServerv4Lease -ComputerName $ComputerName -ScopeId $scope.ScopeId |
        Select-Object `
            @{N='IPAddress';      E={ $_.IPAddress.ToString() }}, `
            @{N='ClientId';       E={ $_.ClientId }}, `
            @{N='HostName';       E={ $_.HostName }}, `
            @{N='AddressState';   E={ $_.AddressState }}, `
            @{N='LeaseExpiryTime';E={ if ($_.LeaseExpiryTime) { $_.LeaseExpiryTime.ToString('o') } else { $null } }}
}

# Always emit an array, even for 0/1 results.
@($leases) | ConvertTo-Json -Depth 3 -Compress
