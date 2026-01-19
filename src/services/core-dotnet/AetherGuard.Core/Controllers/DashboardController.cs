using AetherGuard.Core.Data;
using AetherGuard.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace AetherGuard.Core.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly TelemetryStore _store;
    private readonly ApplicationDbContext _context;

    public DashboardController(TelemetryStore store, ApplicationDbContext context)
    {
        _store = store;
        _context = context;
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

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory()
    {
        var records = await _context.TelemetryRecords
            .OrderByDescending(x => x.Timestamp)
            .Take(20)
            .Reverse()
            .ToListAsync();

        return Ok(records);
    }
}
