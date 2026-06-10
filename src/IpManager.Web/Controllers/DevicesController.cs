using IpManager.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace IpManager.Web.Controllers;

public class DevicesController : Controller
{
    private readonly INetworkStore _store;
    public DevicesController(INetworkStore store) => _store = store;

    public IActionResult Details(int id)
    {
        var device = _store.GetDeviceDetails(id);
        return device is null ? NotFound() : View(device);
    }
}
