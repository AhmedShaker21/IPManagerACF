using IpManager.Core.Abstractions;
using IpManager.Core.Dtos;
using IpManager.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace IpManager.Web.Controllers;

public class AssignController : Controller
{
    private readonly INetworkStore _store;
    private readonly INotificationPublisher _publisher;

    public AssignController(INetworkStore store, INotificationPublisher publisher)
    {
        _store = store;
        _publisher = publisher;
    }

    // GET /Assign?ip=192.168.1.22         -> blank form for that IP
    // GET /Assign?deviceId=5              -> prefilled with an existing device's details
    [HttpGet]
    public IActionResult Index(string? ip, int? deviceId)
    {
        var vm = new AssignViewModel { Ip = ip ?? "" };

        if (deviceId is int id)
        {
            var d = _store.GetDeviceDetails(id);
            if (d is not null)
                vm = new AssignViewModel
                {
                    Ip = d.CurrentIp ?? ip ?? "",
                    Mac = d.Mac == "(manual)" ? null : d.Mac,
                    DeviceName = d.DeviceName, Hostname = d.Hostname, DeviceType = d.DeviceType,
                    Department = d.Department, Location = d.Location,
                    OwnerName = d.OwnerName, OwnerEmail = d.OwnerEmail, OwnerPhone = d.OwnerPhone,
                    Cpu = d.Cpu, RamGb = d.RamGb, StorageGb = d.StorageGb,
                    OperatingSystem = d.OperatingSystem, AssetTag = d.AssetTag, Notes = d.Notes
                };
        }
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(AssignViewModel form)
    {
        if (string.IsNullOrWhiteSpace(form.Ip))
        {
            ModelState.AddModelError(nameof(form.Ip), "An IP address is required.");
            return View(form);
        }

        var events = _store.AssignIp(form.ToRequest());

        await _publisher.PublishStateChangedAsync();
        foreach (var e in events)
            await _publisher.PublishNotificationAsync(
                new NotificationDto(0, e.Type.ToString(), e.Title, e.Message, e.Ip, e.Mac, DateTime.UtcNow, false));

        return RedirectToAction("Index", "Dashboard");
    }
}
