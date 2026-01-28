# Observability (OpenTelemetry)

The Docker Compose stack includes an OpenTelemetry Collector and Jaeger for distributed tracing.

## Components

- `otel-collector`: Receives OTLP on `4317` (gRPC) and `4318` (HTTP).
- `jaeger`: UI at http://localhost:16686.

## Services instrumented

- Core API (.NET): ASP.NET Core, HttpClient, EF Core, runtime/process metrics.
- AI Engine (FastAPI): FastAPI + requests spans.
- Dashboard (Next.js): Node.js auto-instrumentation for server routes.

## Configuration knobs

Core service (`appsettings.json` or environment):

- `OpenTelemetry__Enabled`
- `OpenTelemetry__OtlpEndpoint`
- `OpenTelemetry__Protocol` (`grpc` or `http/protobuf`)

AI + Dashboard (environment):

- `OTEL_EXPORTER_OTLP_ENDPOINT`
- `OTEL_SERVICE_NAME`
- `OTEL_TRACES_EXPORTER`
- `OTEL_METRICS_EXPORTER`

## Troubleshooting

If traces are missing:

- Verify `otel-collector` and `jaeger` are running: `docker compose ps`.
- Check collector logs: `docker compose logs -f otel-collector`.
- Ensure OTLP ports are free (4317/4318).
