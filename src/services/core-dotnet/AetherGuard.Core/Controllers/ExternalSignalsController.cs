using AetherGuard.Core.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AetherGuard.Core.Controllers;

[ApiController]
[Route("api/v1/signals")]
public class ExternalSignalsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<ExternalSignalsController> _logger;

    public ExternalSignalsController(ApplicationDbContext db, ILogger<ExternalSignalsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetSignals(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? source,
        [FromQuery] string? region,
        [FromQuery] string? severity,
        [FromQuery] int limit = 200,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 500);
        var query = _db.ExternalSignals.AsNoTracking();

        if (from.HasValue)
        {
            query = query.Where(signal => signal.PublishedAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(signal => signal.PublishedAt <= to.Value);
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            query = query.Where(signal => signal.Source == source);
        }

        if (!string.IsNullOrWhiteSpace(region))
        {
            query = query.Where(signal => signal.Region == region);
        }

        if (!string.IsNullOrWhiteSpace(severity))
        {
            query = query.Where(signal => signal.Severity == severity);
        }

        var results = await query
            .OrderByDescending(signal => signal.PublishedAt)
            .Take(limit)
            .Select(signal => new
            {
                signal.Id,
                signal.Source,
                signal.ExternalId,
                signal.Title,
                signal.Summary,
                signal.Region,
                signal.Severity,
                signal.Category,
                signal.Url,
                signal.Tags,
                signal.PublishedAt,
                signal.IngestedAt
            })
            .ToListAsync(cancellationToken);

        _logger.LogInformation("Returned {Count} external signals.", results.Count);
        return Ok(results);
    }

    [HttpGet("feeds")]
    public async Task<IActionResult> GetFeedStates(CancellationToken cancellationToken = default)
    {
        var feeds = await _db.ExternalSignalFeedStates
            .AsNoTracking()
            .OrderBy(state => state.Name)
            .Select(state => new
            {
                state.Name,
                state.Url,
                state.LastFetchAt,
                state.LastSuccessAt,
                state.FailureCount,
                state.LastError,
                state.LastStatusCode
            })
            .ToListAsync(cancellationToken);

        return Ok(feeds);
    }
}
