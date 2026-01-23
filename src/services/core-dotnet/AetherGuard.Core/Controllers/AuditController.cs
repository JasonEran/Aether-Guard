using AetherGuard.Core.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AetherGuard.Core.Controllers;

[ApiController]
[Route("api/v1/audits")]
public class AuditController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public AuditController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    [HttpGet("/audits")]
    public async Task<IActionResult> GetAudits(
        [FromQuery] string? action,
        [FromQuery] Guid? commandId,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var clampedLimit = Math.Clamp(limit, 1, 200);
        var query = _context.CommandAudits.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(audit => audit.Action == action);
        }

        if (commandId.HasValue)
        {
            query = query.Where(audit => audit.CommandId == commandId.Value);
        }

        var results = await query
            .OrderByDescending(audit => audit.CreatedAt)
            .Take(clampedLimit)
            .Select(audit => new
            {
                audit.Id,
                audit.CommandId,
                audit.Actor,
                audit.Action,
                audit.Result,
                audit.Error,
                audit.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(results);
    }
}
