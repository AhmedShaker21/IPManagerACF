using IpManager.Core.Abstractions;
using IpManager.Core.Demo;
using IpManager.Core.Services;
using IpManager.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IpManager.Core;

public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the application service and demo engine, plus the in-memory store as the
    /// DEFAULT <see cref="INetworkStore"/>. Because it uses TryAdd, an earlier explicit
    /// registration wins — so calling AddEfCoreStore() first (see IpManager.Persistence.EfCore)
    /// makes the app run on SQL Server instead, with no other change.
    /// </summary>
    public static IServiceCollection AddIpManagerCore(this IServiceCollection services)
    {
        services.TryAddSingleton<INetworkStore, InMemoryNetworkStore>();
        services.AddSingleton<INetworkService, NetworkService>();
        services.AddSingleton<NetworkSimulator>();
        return services;
    }
}
