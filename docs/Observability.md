# Observability (OpenTelemetry)

The Docker Compose stack includes an OpenTelemetry Collector and Jaeger for distributed tracing.

## Components

- `otel-collector`: Receives OTLP on `4317` (gRPC) and `4318` (HTTP).
- `jaeger`: UI at http://localhost:16686.

## Services instrumented

- Core API (.NET): ASP.NET Core, HttpClient, EF Core, runtime/process metrics.
- AI Engine (FastAPI): FastAPI + requests spans.
- Dashboard (Next.js): Node.js auto-instrumentation for server routes.
- External Signals pipeline:
  - Core custom traces/metrics for summarize + enrich (batch and fallback paths).
  - AI custom traces/metrics for `/signals/enrich`, `/signals/enrich/batch`, and `/signals/summarize`.

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

## Custom metric names (v2.3 Milestone 1)

Core (`AetherGuard.Core.ExternalSignals` meter):

- `aetherguard.external_signals.client.requests`
- `aetherguard.external_signals.client.failures`
- `aetherguard.external_signals.client.duration.ms`
- `aetherguard.external_signals.client.documents`
- `aetherguard.external_signals.pipeline.runs`
- `aetherguard.external_signals.pipeline.fallbacks`
- `aetherguard.external_signals.pipeline.duration.ms`
- `aetherguard.external_signals.pipeline.batch.size`
- `aetherguard.external_signals.pipeline.updates`

AI (`aether_guard.ai.signals` meter):

- `aetherguard.ai.signals.requests`
- `aetherguard.ai.signals.errors`
- `aetherguard.ai.signals.duration.ms`
- `aetherguard.ai.signals.documents`

## Trace entry points (v2.3 Milestone 1)

- Core spans: `external_signals.client.*`, `external_signals.pipeline.enrich`
- AI spans: `ai.signals.enrich`, `ai.signals.enrich.batch`, `ai.signals.summarize`

## Troubleshooting

If traces are missing:

- Verify `otel-collector` and `jaeger` are running: `docker compose ps`.
- Check collector logs: `docker compose logs -f otel-collector`.
- Ensure OTLP ports are free (4317/4318).

Quick trace check:

1. Trigger signal enrichment by enabling external signals and waiting for one ingest cycle.
2. Open Jaeger (`http://localhost:16686`) and search services:
   - `aether-guard-core`
   - `aether-guard-ai`
3. Validate a trace chain includes Core outbound enrichment call and AI endpoint span.
