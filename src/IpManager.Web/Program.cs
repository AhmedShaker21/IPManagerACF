using IpManager.Core;
using IpManager.Core.Abstractions;
using IpManager.Core.Enums;
using IpManager.Core.Options;
using IpManager.Persistence.EfCore;
using IpManager.Web.Hubs;
using IpManager.Web.Services;
using IpManager.Web.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

builder.Services.Configure<NetworkOptions>(builder.Configuration.GetSection(NetworkOptions.SectionName));
var network = builder.Configuration.GetSection(NetworkOptions.SectionName).Get<NetworkOptions>() ?? new NetworkOptions();

// Live updates over SignalR.
builder.Services.AddSingleton<INotificationPublisher, SignalRNotificationPublisher>();

// Domain services + the default in-memory store.
// To run on SQL Server: add a reference to IpManager.Persistence.EfCore and call
//   builder.Services.AddEfCoreStore(builder.Configuration);   // registers EfNetworkStore
//   builder.Services.AddLiveCollectors(builder.Configuration); // ARP / DHCP / SNMP / syslog
// BEFORE AddIpManagerCore(), then set Network:Mode = "Live".
builder.Services.AddEfCoreStore(builder.Configuration);
builder.Services.AddLiveCollectors(builder.Configuration);
builder.Services.AddIpManagerCore();

if (network.Mode == RunMode.Demo)
{
    builder.Services.AddHostedService<DemoWorker>();
}
else
{
    builder.Services.AddHostedService<NetworkScanWorker>();
    builder.Services.AddHostedService<DhcpSyncWorker>();
    builder.Services.AddHostedService<InternetActivityWorker>();
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");
app.MapHub<NetworkHub>("/hubs/network");

app.Run();
