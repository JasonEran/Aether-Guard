using AetherGuard.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace AetherGuard.Core.Services;

public sealed class MigrationCycleService : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MigrationCycleService> _logger;
    private readonly IConfiguration _configuration;

    public MigrationCycleService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<MigrationCycleService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var orchestrator = scope.ServiceProvider.GetRequiredService<MigrationOrchestrator>();

                var agents = await context.Agents
                    .AsNoTracking()
                    .Select(agent => agent.Id)
                    .ToListAsync(stoppingToken);

                foreach (var agentId in agents)
                {
                    await orchestrator.RunMigrationCycle(agentId.ToString(), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Migration cycle execution failed.");
            }

            await Task.Delay(GetInterval(), stoppingToken);
        }
    }

    private TimeSpan GetInterval()
    {
        if (int.TryParse(_configuration["MigrationIntervalSeconds"], out var seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return DefaultInterval;
    }
}
