using AetherGuard.Core.Data;
using AetherGuard.Core.Observability;
using AetherGuard.Core.Security;
using AetherGuard.Core.Services;
using AetherGuard.Core.Services.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

Activity.DefaultIdFormat = ActivityIdFormat.W3C;
Activity.ForceDefaultIdFormat = true;

var mtlsOptions = builder.Configuration.GetSection("Security:Mtls").Get<MtlsOptions>() ?? new MtlsOptions();
if (mtlsOptions.Enabled)
{
    if (string.IsNullOrWhiteSpace(mtlsOptions.CertificatePath)
        || string.IsNullOrWhiteSpace(mtlsOptions.KeyPath)
        || string.IsNullOrWhiteSpace(mtlsOptions.BundlePath))
    {
        throw new InvalidOperationException("mTLS is enabled but certificate paths are not configured.");
    }

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(mtlsOptions.Port, listenOptions =>
        {
            listenOptions.UseHttps(httpsOptions =>
            {
                httpsOptions.ClientCertificateMode = mtlsOptions.RequireClientCertificate
                    ? ClientCertificateMode.RequireCertificate
                    : ClientCertificateMode.AllowCertificate;
                httpsOptions.ServerCertificateSelector = (_, _) =>
                    MtlsCertificateLoader.LoadServerCertificate(mtlsOptions);
                httpsOptions.ClientCertificateValidation = (cert, _, _) =>
                    MtlsCertificateLoader.ValidateClientCertificate(cert, mtlsOptions);
            });
        });
    });
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddGrpc().AddJsonTranscoding();

builder.Services.AddSingleton<TelemetryStore>();
builder.Services.AddHttpClient<AnalysisService>();
builder.Services.AddScoped<AgentWorkflowService>();
builder.Services.AddScoped<TelemetryIngestionService>();
builder.Services.AddScoped<ControlPlaneService>();
builder.Services.AddScoped<CommandService>();
builder.Services.AddScoped<MigrationOrchestrator>();
builder.Services.AddScoped<DiagnosticsBundleService>();
builder.Services.AddSingleton<SnapshotStorageService>();
builder.Services.AddSingleton<IMessageProducer, RabbitMQProducer>();
builder.Services.AddHostedService<TelemetryProcessor>();
builder.Services.AddHostedService<MigrationCycleService>();
builder.Services.AddHostedService<SnapshotRetentionService>();
builder.Services.AddSingleton<AetherGuard.Core.Services.SchemaRegistry.SchemaRegistryService>();
builder.Services.AddHostedService<AetherGuard.Core.Services.SchemaRegistry.SchemaRegistrySeeder>();

var otelOptions = builder.Configuration.GetSection("OpenTelemetry").Get<OpenTelemetryOptions>()
    ?? new OpenTelemetryOptions();
if (otelOptions.Enabled)
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resourceBuilder =>
        {
            resourceBuilder.AddService(
                serviceName: otelOptions.ServiceName,
                serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
                serviceInstanceId: Environment.MachineName);
        })
        .WithTracing(tracerProviderBuilder =>
        {
            tracerProviderBuilder
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .AddSource("AetherGuard.Core.Messaging")
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(otelOptions.OtlpEndpoint);
                    options.Protocol = otelOptions.Protocol.Equals("http/protobuf", StringComparison.OrdinalIgnoreCase)
                        ? OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf
                        : OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                });
        })
        .WithMetrics(metricProviderBuilder =>
        {
            metricProviderBuilder
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddProcessInstrumentation()
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(otelOptions.OtlpEndpoint);
                    options.Protocol = otelOptions.Protocol.Equals("http/protobuf", StringComparison.OrdinalIgnoreCase)
                        ? OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf
                        : OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                });
        });
}

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("DefaultConnection is not configured.");
}

var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (string.IsNullOrWhiteSpace(redisConnection))
{
    throw new InvalidOperationException("Redis connection string is not configured.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddDbContextFactory<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConnection));

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowDashboard", policy =>
    {
        policy.WithOrigins("http://localhost:3000")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!mtlsOptions.Enabled || !mtlsOptions.DisableHttpsRedirection)
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowDashboard");
app.UseAuthorization();

app.MapGrpcService<AetherGuard.Core.Grpc.AgentGrpcService>();
app.MapGrpcService<AetherGuard.Core.Grpc.ControlPlaneGrpcService>();
app.MapControllers();

app.Run();
