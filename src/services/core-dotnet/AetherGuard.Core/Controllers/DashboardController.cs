using AetherGuard.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace AetherGuard.Core.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly TelemetryStore _store;

    public DashboardController(TelemetryStore store)
    {
        _store = store;
    }

    [HttpGet("latest")]
    public IActionResult GetLatest()
    {
        var latest = _store.GetLatest();
        if (latest is null)
        {
            return NotFound();
        }

        return Ok(latest);
    }
}
