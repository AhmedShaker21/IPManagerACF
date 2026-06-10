using IpManager.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace IpManager.Web.Controllers;

public class ConflictsController : Controller
{
    private readonly INetworkStore _store;
    public ConflictsController(INetworkStore store) => _store = store;

    public IActionResult Index(bool includeResolved = false)
    {
        ViewBag.IncludeResolved = includeResolved;
        return View(_store.GetConflictViews(includeResolved));
    }
}
