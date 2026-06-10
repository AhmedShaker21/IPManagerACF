using IpManager.Core.Abstractions;
using IpManager.Core.Dtos;
using IpManager.Core.Options;
using Microsoft.Extensions.Options;

namespace IpManager.Core.Services;

public sealed class NetworkService : INetworkService
{
    private readonly INetworkStore _store;
    private readonly INotificationPublisher _publisher;
    private readonly NetworkOptions _opts;

    public NetworkService(INetworkStore store, INotificationPublisher publisher, IOptions<NetworkOptions> opts)
    {
        _store = store;
        _publisher = publisher;
        _opts = opts.Value;
    }

    public void EnsureSeeded() => _store.EnsureSeeded(_opts.Subnets);

    public Task ApplyObservationAsync(NetworkObservation observation) =>
        PublishAsync(_store.ApplyObservation(observation));

    public async Task RunScanCycleAsync(IReadOnlyList<NetworkObservation> observations, IReadOnlySet<string> seenIps)
    {
        var events = new List<DomainEvent>();
        foreach (var o in observations)
            events.AddRange(_store.ApplyObservation(o));
        events.AddRange(_store.ExpireStale(seenIps, DateTime.UtcNow, _opts.MissedScansBeforeFree));
        events.AddRange(_store.ScanConflicts());
        await PublishAsync(events);
    }

    public Task ApplyInternetEventAsync(InternetEvent ev) =>
        PublishAsync(_store.ApplyInternetEvent(ev));

    public Task DetectConflictsAsync() => PublishAsync(_store.ScanConflicts());

    private async Task PublishAsync(IReadOnlyList<DomainEvent> events)
    {
        foreach (var e in events)
            await _publisher.PublishNotificationAsync(
                new NotificationDto(0, e.Type.ToString(), e.Title, e.Message, e.Ip, e.Mac, DateTime.UtcNow, false));
        if (events.Count > 0)
            await _publisher.PublishStateChangedAsync();
    }
}
