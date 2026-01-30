using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AetherGuard.Core.Services.SchemaRegistry;

public sealed class SchemaRegistrySeeder : IHostedService
{
    private readonly SchemaRegistryService _registryService;
    private readonly ILogger<SchemaRegistrySeeder> _logger;

    public SchemaRegistrySeeder(SchemaRegistryService registryService, ILogger<SchemaRegistrySeeder> logger)
    {
        _registryService = registryService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Seeding schema registry entries.");
        await _registryService.EnsureTelemetrySchemaAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
