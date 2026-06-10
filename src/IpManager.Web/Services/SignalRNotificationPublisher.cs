using IpManager.Core.Abstractions;
using IpManager.Core.Dtos;
using IpManager.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace IpManager.Web.Services;

public sealed class SignalRNotificationPublisher : INotificationPublisher
{
    private readonly IHubContext<NetworkHub> _hub;
    public SignalRNotificationPublisher(IHubContext<NetworkHub> hub) => _hub = hub;

    public Task PublishNotificationAsync(NotificationDto notification) =>
        _hub.Clients.All.SendAsync("notify", notification);

    public Task PublishStateChangedAsync() =>
        _hub.Clients.All.SendAsync("stateChanged");
}
