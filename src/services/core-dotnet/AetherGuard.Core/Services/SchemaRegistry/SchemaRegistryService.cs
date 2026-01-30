using System.Collections.Concurrent;
using System.Text.Json;
using AetherGuard.Core.Data;
using AetherGuard.Core.Models;
using Json.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AetherGuard.Core.Services.SchemaRegistry;

public sealed class SchemaRegistryService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly SchemaRegistryOptions _options;
    private readonly ILogger<SchemaRegistryService> _logger;
    private readonly ConcurrentDictionary<string, JsonSchema> _schemaCache = new();
    private static readonly EvaluationOptions ValidationOptions = new()
    {
        OutputFormat = OutputFormat.Flag
    };

    public SchemaRegistryService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IConfiguration configuration,
        ILogger<SchemaRegistryService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _options = configuration.GetSection("SchemaRegistry").Get<SchemaRegistryOptions>()
            ?? new SchemaRegistryOptions();
    }

    public async Task<bool> IsVersionSupportedAsync(string subject, int version, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return true;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.SchemaRegistryEntries.AnyAsync(
            entry => entry.Subject == subject && entry.Version == version,
            cancellationToken);
    }

    public async Task EnsureTelemetrySchemaAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        await EnsureSchemaAsync(
            TelemetrySchemaDefinitions.TelemetryEnvelopeSubject,
            TelemetrySchemaDefinitions.TelemetryEnvelopeVersion,
            TelemetrySchemaDefinitions.TelemetryEnvelopeV1,
            cancellationToken);

        await EnsureSchemaAsync(
            TelemetrySchemaDefinitions.TelemetryPayloadSubject,
            TelemetrySchemaDefinitions.TelemetryPayloadVersion,
            TelemetrySchemaDefinitions.TelemetryPayloadV1,
            cancellationToken);
    }

    public async Task EnsureSchemaAsync(
        string subject,
        int version,
        string schema,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var exists = await db.SchemaRegistryEntries.AnyAsync(
            entry => entry.Subject == subject && entry.Version == version,
            cancellationToken);

        if (exists)
        {
            return;
        }

        db.SchemaRegistryEntries.Add(new SchemaRegistryEntry
        {
            Subject = subject,
            Version = version,
            Schema = schema,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Registered schema {Subject} v{Version}.", subject, version);
    }

    public async Task<bool> ValidateAsync(
        string subject,
        int version,
        JsonElement instance,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return true;
        }

        var schema = await GetSchemaAsync(subject, version, cancellationToken);
        if (schema is null)
        {
            return false;
        }

        var result = schema.Evaluate(instance, ValidationOptions);
        if (!result.IsValid)
        {
            _logger.LogWarning("Schema validation failed for {Subject} v{Version}.", subject, version);
        }

        return result.IsValid;
    }

    private async Task<JsonSchema?> GetSchemaAsync(
        string subject,
        int version,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"{subject}:{version}";
        if (_schemaCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var entry = await db.SchemaRegistryEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Subject == subject && candidate.Version == version,
                cancellationToken);

        if (entry is null)
        {
            return null;
        }

        try
        {
            var schema = JsonSchema.FromText(entry.Schema);
            _schemaCache.TryAdd(cacheKey, schema);
            return schema;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse schema {Subject} v{Version}.", subject, version);
            return null;
        }
    }
}
