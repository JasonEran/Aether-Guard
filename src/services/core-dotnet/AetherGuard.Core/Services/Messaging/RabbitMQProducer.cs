using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AetherGuard.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace AetherGuard.Core.Services.Messaging;

public sealed class RabbitMQProducer : IMessageProducer, IDisposable
{
    private const string TraceParentHeader = "traceparent";
    private const string TraceStateHeader = "tracestate";
    private const string SchemaVersionHeader = "schema_version";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<RabbitMQProducer> _logger;
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly object _lock = new();
    private readonly TelemetrySchemaOptions _schemaOptions;
    private readonly TelemetryQueueOptions _queueOptions;

    public RabbitMQProducer(IConfiguration configuration, ILogger<RabbitMQProducer> logger)
    {
        _logger = logger;
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

    public void SendMessage<T>(T message)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.Serialize(
            new TelemetryEnvelope(
                SchemaVersion: _schemaOptions.CurrentVersion,
                SentAt: now.ToUnixTimeSeconds(),
                Payload: (message as TelemetryPayload)
                         ?? throw new InvalidOperationException("Telemetry payload is required.")),
            JsonOptions);
        var body = Encoding.UTF8.GetBytes(payload);

        var properties = new BasicProperties
        {
            Persistent = true
        };

        InjectTraceContext(properties);
        InjectSchemaVersion(properties);

        lock (_lock)
        {
            _channel.BasicPublishAsync(
                exchange: "",
                routingKey: _queueOptions.QueueName,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    private static void InjectTraceContext(IBasicProperties properties)
    {
        var current = Activity.Current;
        if (current is null)
        {
            return;
        }

        properties.Headers ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var traceParent = BuildTraceParent(current.Context);
        properties.Headers[TraceParentHeader] = Encoding.UTF8.GetBytes(traceParent);

        if (!string.IsNullOrWhiteSpace(current.TraceStateString))
        {
            properties.Headers[TraceStateHeader] = Encoding.UTF8.GetBytes(current.TraceStateString);
        }
    }

    private void InjectSchemaVersion(IBasicProperties properties)
    {
        properties.Headers ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        properties.Headers[SchemaVersionHeader] = _schemaOptions.CurrentVersion;
    }

    private static string BuildTraceParent(ActivityContext context)
    {
        var flags = context.TraceFlags.HasFlag(ActivityTraceFlags.Recorded) ? "01" : "00";
        return $"00-{context.TraceId.ToHexString()}-{context.SpanId.ToHexString()}-{flags}";
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
    }

    public void Dispose()
    {
        _channel.Dispose();
        _connection.Dispose();
    }
}
