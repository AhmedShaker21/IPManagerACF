using IpManager.Core.Abstractions;
using IpManager.Core.Options;
using IpManager.Persistence.EfCore.Collectors;
using IpManager.Persistence.EfCore.Data;
using IpManager.Persistence.EfCore.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IpManager.Persistence.EfCore;

public static class EfCoreServiceCollectionExtensions
{
    /// <summary>
    /// Swaps the default in-memory store for SQL Server. Call this BEFORE AddIpManagerCore()
    /// (Core registers the in-memory store with TryAdd, so the first registration — this one — wins).
    /// </summary>
    public static IServiceCollection AddEfCoreStore(this IServiceCollection services, IConfiguration config)
    {
        var cs = config.GetConnectionString("Default")
                 ?? throw new InvalidOperationException("ConnectionStrings:Default is required for the SQL Server store.");

        services.AddDbContextFactory<AppDbContext>(o => o.UseSqlServer(cs));
        services.AddSingleton<INetworkStore, EfNetworkStore>();
        return services;
    }

    /// <summary>
    /// Registers the real network collectors. Each is gated by its Enabled flag in config, so you
    /// can turn on only what your environment supports (e.g. SNMP + syslog, but no Windows DHCP).
    /// </summary>
    public static IServiceCollection AddLiveCollectors(this IServiceCollection services, IConfiguration config)
    {
        var net = config.GetSection(NetworkOptions.SectionName).Get<NetworkOptions>() ?? new NetworkOptions();

        // same-subnet discovery — always useful
        services.AddSingleton<INetworkScanner, PingSweepScanner>();
        services.AddSingleton<IArpReader, ArpTableReader>();

        // cross-subnet MAC discovery
        if (net.Snmp.Enabled)
            services.AddSingleton<ISnmpArpReader, SnmpArpReader>();

        // rich bindings straight from DHCP
        if (net.Dhcp.Enabled)
            services.AddSingleton<IDhcpLeaseReader, WindowsDhcpLeaseReader>();

        // per-device internet usage from the gateway
        if (net.Syslog.Enabled)
            services.AddSingleton<IInternetActivityReader, SyslogInternetActivityReader>();

        return services;
    }
}
