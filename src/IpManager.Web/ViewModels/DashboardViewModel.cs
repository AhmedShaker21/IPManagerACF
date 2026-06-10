using IpManager.Core.Dtos;

namespace IpManager.Web.ViewModels;

public class DashboardViewModel
{
    public required DashboardSnapshot Snapshot { get; init; }
    public required PagedResult<IpRow> Rows { get; init; }
    public string? Search { get; init; }
    public string? Status { get; init; }
}
