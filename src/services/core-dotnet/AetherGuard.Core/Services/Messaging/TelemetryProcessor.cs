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
                var body = Encoding.UTF8.GetString(ea.Body.Span);
                var payload = JsonSerializer.Deserialize<TelemetryPayload>(body, JsonOptions);

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
}
