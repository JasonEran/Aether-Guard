using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AetherGuard.Core.Data;
using AetherGuard.Core.Models;
using AetherGuard.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace AetherGuard.Core.Services.Messaging;

public sealed class TelemetryProcessor : BackgroundService
{
    private const string QueueName = "telemetry_data";
    private const string TraceParentHeader = "traceparent";
    private const string TraceStateHeader = "tracestate";
    private const string SchemaVersionHeader = "schema_version";
    private const int CurrentSchemaVersion = 1;
    private const int MinSupportedSchemaVersion = 1;
    private static readonly ActivitySource ActivitySource = new("AetherGuard.Core.Messaging");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<TelemetryProcessor> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConnection _connection;
    private readonly IChannel _channel;

    public TelemetryProcessor(IConfiguration configuration, IServiceProvider serviceProvider, ILogger<TelemetryProcessor> logger)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;

        var section = configuration.GetSection("RabbitMq");
        var host = section["Host"] ?? "localhost";
        var user = section["User"] ?? "guest";
        var password = section["Password"] ?? "guest";
        var portValue = section["Port"];
        var port = int.TryParse(portValue, out var parsedPort) ? parsedPort : 5672;

        var factory = new ConnectionFactory
        {
            HostName = host,
            UserName = user,
            Password = password,
            Port = port,
            AutomaticRecoveryEnabled = true
        };

        _connection = factory.CreateConnectionAsync(CancellationToken.None).GetAwaiter().GetResult();
        _channel = _connection.CreateChannelAsync(
                new CreateChannelOptions(
                    publisherConfirmationsEnabled: false,
                    publisherConfirmationTrackingEnabled: false,
                    outstandingPublisherConfirmationsRateLimiter: null,
                    consumerDispatchConcurrency: null),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        _channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            noWait: false,
            cancellationToken: CancellationToken.None).GetAwaiter().GetResult();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var parentContext = ExtractTraceContext(ea.BasicProperties?.Headers);
                using var activity = ActivitySource.StartActivity(
                    "telemetry.consume",
                    ActivityKind.Consumer,
                    parentContext);

                activity?.SetTag("messaging.system", "rabbitmq");
                activity?.SetTag("messaging.destination", QueueName);

                var body = Encoding.UTF8.GetString(ea.Body.Span);
                if (!TryDeserializePayload(body, ea.BasicProperties?.Headers, out var payload, out var schemaVersion))
                {
                    _logger.LogWarning("Dropped telemetry message with unsupported schema.");
                    activity?.SetTag("telemetry.schema.version", schemaVersion);
                    await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: CancellationToken.None);
                    return;
                }

                activity?.SetTag("telemetry.schema.version", schemaVersion);

                if (payload is null)
                {
                    _logger.LogWarning("Received empty telemetry payload");
                    await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: CancellationToken.None);
                    return;
                }

                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var analysisService = scope.ServiceProvider.GetRequiredService<AnalysisService>();
                var telemetryStore = scope.ServiceProvider.GetRequiredService<TelemetryStore>();

                var analysis = await analysisService.AnalyzeAsync(payload);
                var status = analysis.Status;
                var confidence = analysis.Confidence;

                var record = new TelemetryRecord
                {
                    AgentId = payload.AgentId,
                    WorkloadTier = payload.WorkloadTier,
                    RebalanceSignal = payload.RebalanceSignal,
                    DiskAvailable = payload.DiskAvailable,
                    CpuUsage = 0,
                    MemoryUsage = 0,
                    AiStatus = status,
                    AiConfidence = confidence,
                    RootCause = analysis.RootCause,
                    PredictedCpu = analysis.Prediction,
                    Timestamp = DateTimeOffset.FromUnixTimeSeconds(payload.Timestamp).UtcDateTime
                };

                db.TelemetryRecords.Add(record);
                await db.SaveChangesAsync(stoppingToken);

                telemetryStore.Update(payload, analysis);

                await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process telemetry message");
                await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: CancellationToken.None);
            }
        };

        await _channel.BasicConsumeAsync(queue: QueueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override void Dispose()
    {
        _channel.Dispose();
        _connection.Dispose();
        base.Dispose();
    }

    private static ActivityContext ExtractTraceContext(IDictionary<string, object?>? headers)
    {
        if (headers is null)
        {
            return default;
        }

        if (!TryGetHeaderValue(headers, TraceParentHeader, out var traceParent))
        {
            return default;
        }

        TryGetHeaderValue(headers, TraceStateHeader, out var traceState);

        return ActivityContext.TryParse(traceParent, traceState, out var context)
            ? context
            : default;
    }

    private static bool TryGetHeaderValue(
        IDictionary<string, object?> headers,
        string key,
        out string value)
    {
        value = string.Empty;
        if (!headers.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case byte[] bytes:
                value = Encoding.UTF8.GetString(bytes);
                return true;
            case ReadOnlyMemory<byte> memory:
                value = Encoding.UTF8.GetString(memory.Span);
                return true;
            case string str:
                value = str;
                return true;
            default:
                return false;
        }
    }

    private static bool TryDeserializePayload(
        string body,
        IDictionary<string, object?>? headers,
        out TelemetryPayload? payload,
        out int schemaVersion)
    {
        payload = null;
        schemaVersion = ResolveSchemaVersion(headers);

        if (!IsSchemaSupported(schemaVersion))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("payload", out var payloadElement))
            {
                var envelope = JsonSerializer.Deserialize<TelemetryEnvelope>(body, JsonOptions);
                if (envelope is null || !IsSchemaSupported(envelope.SchemaVersion))
                {
                    schemaVersion = envelope?.SchemaVersion ?? schemaVersion;
                    return false;
                }

                payload = envelope.Payload;
                schemaVersion = envelope.SchemaVersion;
                return true;
            }

            payload = JsonSerializer.Deserialize<TelemetryPayload>(body, JsonOptions);
            return payload is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int ResolveSchemaVersion(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue(SchemaVersionHeader, out var raw) || raw is null)
        {
            return CurrentSchemaVersion;
        }

        return raw switch
        {
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var value) => value,
            ReadOnlyMemory<byte> memory when int.TryParse(Encoding.UTF8.GetString(memory.Span), out var value) => value,
            string str when int.TryParse(str, out var value) => value,
            int value => value,
            long value => (int)value,
            _ => CurrentSchemaVersion
        };
    }

    private static bool IsSchemaSupported(int version)
        => version >= MinSupportedSchemaVersion && version <= CurrentSchemaVersion;
}
