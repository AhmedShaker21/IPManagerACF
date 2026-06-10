using IpManager.Core.Demo;
using IpManager.Core.Options;
using Microsoft.Extensions.Options;

namespace IpManager.Web.Workers;

/// <summary>Runs only in Demo mode. Seeds the sample network, then drives live changes.</summary>
public sealed class DemoWorker : BackgroundService
{
    private readonly NetworkSimulator _sim;
    private readonly NetworkOptions _opts;
    private readonly ILogger<DemoWorker> _log;

    public DemoWorker(NetworkSimulator sim, IOptions<NetworkOptions> opts, ILogger<DemoWorker> log)
    {
        _sim = sim; _opts = opts.Value; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        _log.LogInformation("Demo mode: seeding sample network for مصنع الطائرات");
        await _sim.SeedAsync();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _opts.Demo.TickSeconds)));
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                try { await _sim.TickAsync(); }
                catch (Exception ex) { _log.LogError(ex, "Demo tick failed"); }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }
}
