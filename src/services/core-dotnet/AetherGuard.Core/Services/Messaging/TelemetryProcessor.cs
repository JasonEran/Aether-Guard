using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Linq;
using AetherGuard.Core.Data;
using AetherGuard.Core.Models;
using AetherGuard.Core.Services;
using AetherGuard.Core.Services.SchemaRegistry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace AetherGuard.Core.Services.Messaging;

public sealed class TelemetryProcessor : BackgroundService
{
    private const string TraceParentHeader = "traceparent";
    private const string TraceStateHeader = "tracestate";
    private const string SchemaVersionHeader = "schema_version";
    private static readonly string[] RequeueKeywords = ["requeue", "retry"];
    private const string TelemetrySchemaSubject = TelemetrySchemaDefinitions.TelemetryEnvelopeSubject;
    private const string TelemetryPayloadSubject = TelemetrySchemaDefinitions.TelemetryPayloadSubject;
    private static readonly ActivitySource ActivitySource = new("AetherGuard.Core.Messaging");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<TelemetryProcessor> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly TelemetrySchemaOptions _schemaOptions;
    private readonly TelemetryQueueOptions _queueOptions;
    private readonly SchemaRegistryService _schemaRegistry;

    public TelemetryProcessor(
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        SchemaRegistryService schemaRegistry,
        ILogger<TelemetryProcessor> logger)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _schemaRegistry = schemaRegistry;
        _schemaOptions = configuration.GetSection("TelemetrySchema").Get<TelemetrySchemaOptions>()
            ?? new TelemetrySchemaOptions();
        _queueOptions = configuration.GetSection("TelemetryQueue").Get<TelemetryQueueOptions>()
            ?? new TelemetryQueueOptions();

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
        ConfigureTelemetryQueueAsync().GetAwaiter().GetResult();
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
                activity?.SetTag("messaging.destination", _queueOptions.QueueName);

                var body = Encoding.UTF8.GetString(ea.Body.Span);
                using var document = JsonDocument.Parse(body);
                if (!TryDeserializePayload(
                        document.RootElement,
                        ea.BasicProperties?.Headers,
                        out var payload,
                        out var schemaVersion,
                        out var isEnvelope))
                {
                    _logger.LogWarning("Dropped telemetry message with unsupported schema.");
                    activity?.SetTag("telemetry.schema.version", schemaVersion);
                    await HandleUnsupportedSchemaAsync(ea.DeliveryTag, stoppingToken);
                    return;
                }

                var schemaSubject = isEnvelope ? TelemetrySchemaSubject : TelemetryPayloadSubject;
                activity?.SetTag("telemetry.schema.version", schemaVersion);
                activity?.SetTag("telemetry.schema.subject", schemaSubject);
                if (!await _schemaRegistry.ValidateAsync(schemaSubject, schemaVersion, document.RootElement, stoppingToken))
                {
                    _logger.LogWarning("Telemetry schema {Subject} v{Version} failed validation.", schemaSubject, schemaVersion);
                    await HandleUnsupportedSchemaAsync(ea.DeliveryTag, stoppingToken);
                    return;
                }

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

        await _channel.BasicConsumeAsync(
            queue: _queueOptions.QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

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

    private bool TryDeserializePayload(
        JsonElement root,
        IDictionary<string, object?>? headers,
        out TelemetryPayload? payload,
        out int schemaVersion,
        out bool isEnvelope)
    {
        payload = null;
        isEnvelope = false;
        schemaVersion = ResolveSchemaVersion(headers);

        try
        {
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("payload", out _))
            {
                isEnvelope = true;
                var envelope = root.Deserialize<TelemetryEnvelope>(JsonOptions);
                if (envelope is null || !IsSchemaSupported(envelope.SchemaVersion))
                {
                    schemaVersion = envelope?.SchemaVersion ?? schemaVersion;
                    return false;
                }

                payload = envelope.Payload;
                schemaVersion = envelope.SchemaVersion;
                return payload is not null;
            }

            payload = root.Deserialize<TelemetryPayload>(JsonOptions);
            return payload is not null && IsSchemaSupported(schemaVersion);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private int ResolveSchemaVersion(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue(SchemaVersionHeader, out var raw) || raw is null)
        {
            return _schemaOptions.CurrentVersion;
        }

        return raw switch
        {
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var value) => value,
            ReadOnlyMemory<byte> memory when int.TryParse(Encoding.UTF8.GetString(memory.Span), out var value) => value,
            string str when int.TryParse(str, out var value) => value,
            int value => value,
            long value => (int)value,
            _ => _schemaOptions.CurrentVersion
        };
    }

    private bool IsSchemaSupported(int version)
        => version >= _schemaOptions.MinSupportedVersion && version <= _schemaOptions.MaxSupportedVersion;

    private async Task HandleUnsupportedSchemaAsync(ulong deliveryTag, CancellationToken cancellationToken)
    {
        if (ShouldRequeueUnsupported())
        {
            await _channel.BasicNackAsync(
                deliveryTag,
                multiple: false,
                requeue: true,
                cancellationToken: cancellationToken);
            return;
        }

        await _channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken: cancellationToken);
    }

    private bool ShouldRequeueUnsupported()
    {
        if (string.IsNullOrWhiteSpace(_schemaOptions.OnUnsupported))
        {
            return false;
        }

        return RequeueKeywords.Any(keyword =>
            _schemaOptions.OnUnsupported.Trim().Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private async Task ConfigureTelemetryQueueAsync()
    {
        IDictionary<string, object?>? arguments = null;

        if (_queueOptions.EnableDeadLettering)
        {
            await _channel.ExchangeDeclareAsync(
                exchange: _queueOptions.DeadLetterExchange,
                type: ExchangeType.Direct,
                durable: true,
                autoDelete: false,
                cancellationToken: CancellationToken.None);

            await _channel.QueueDeclareAsync(
                queue: _queueOptions.DeadLetterQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                noWait: false,
                cancellationToken: CancellationToken.None);

            await _channel.QueueBindAsync(
                queue: _queueOptions.DeadLetterQueueName,
                exchange: _queueOptions.DeadLetterExchange,
                routingKey: _queueOptions.DeadLetterRoutingKey,
                cancellationToken: CancellationToken.None);

            arguments = new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = _queueOptions.DeadLetterExchange,
                ["x-dead-letter-routing-key"] = _queueOptions.DeadLetterRoutingKey
            };
        }

        try
        {
            await _channel.QueueDeclareAsync(
                queue: _queueOptions.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: arguments,
                noWait: false,
                cancellationToken: CancellationToken.None);
        }
        catch (OperationInterruptedException ex) when (_queueOptions.EnableDeadLettering)
        {
            _logger.LogWarning(ex, "Telemetry queue declare with DLQ failed; retrying without DLQ arguments.");
            await _channel.QueueDeclareAsync(
                queue: _queueOptions.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                noWait: false,
                cancellationToken: CancellationToken.None);
        }

        if (_queueOptions.PrefetchCount > 0)
        {
            await _channel.BasicQosAsync(
                prefetchSize: 0,
                prefetchCount: _queueOptions.PrefetchCount,
                global: false,
                cancellationToken: CancellationToken.None);
        }
    }
}
