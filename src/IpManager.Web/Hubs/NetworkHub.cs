using Microsoft.AspNetCore.SignalR;

namespace IpManager.Web.Hubs;

/// <summary>Connected dashboards listen here for "notify" and "stateChanged" messages.</summary>
public sealed class NetworkHub : Hub { }
