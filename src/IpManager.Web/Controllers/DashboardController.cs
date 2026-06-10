using IpManager.Core.Abstractions;
using IpManager.Core.Dtos;
using IpManager.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace IpManager.Web.Controllers;

public class DashboardController : Controller
{
    private readonly INetworkStore _store;
    private const int PageSize = 25;

    public DashboardController(INetworkStore store) => _store = store;

    public IActionResult Index(string? search, string? status, int page = 1)
    {
        var vm = new DashboardViewModel
        {
            Snapshot = _store.GetDashboard(),
            Rows = _store.QueryIpRows(new IpQuery(search, status, page, PageSize)),
            Search = search,
            Status = status
        };
        return View(vm);
    }

    // AJAX: refreshed table body on search / sort / paging / live change
    public IActionResult Table(string? search, string? status, int page = 1)
        => PartialView("_IpTable", _store.QueryIpRows(new IpQuery(search, status, page, PageSize)));

    // AJAX: stat cards + live scope grid, refreshed on every state change
    public IActionResult LivePanel()
        => PartialView("_LivePanel", _store.GetDashboard());

    [HttpGet]
    public IActionResult Notifications(int take = 20)
        => Json(_store.GetNotifications(take));

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public IActionResult MarkRead()
    {
        _store.MarkNotificationsRead();
        return Ok();
    }
}
