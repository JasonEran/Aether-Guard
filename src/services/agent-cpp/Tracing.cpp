#include "Tracing.hpp"

#include <chrono>
#include <iomanip>
#include <random>
#include <sstream>

#if AETHER_ENABLE_OTEL
#include <opentelemetry/exporters/otlp/otlp_http_exporter.h>
#include <opentelemetry/sdk/resource/resource.h>
#include <opentelemetry/sdk/trace/batch_span_processor.h>
#include <opentelemetry/sdk/trace/tracer_provider.h>
#include <opentelemetry/trace/provider.h>
#include <opentelemetry/trace/span_context.h>
#include <opentelemetry/trace/status_code.h>
#endif

namespace {
std::string RandomHex(size_t bytes) {
    static thread_local std::mt19937_64 rng{std::random_device{}()};
    std::uniform_int_distribution<int> dist(0, 255);

    std::ostringstream out;
    out << std::hex << std::nouppercase;
    for (size_t i = 0; i < bytes; ++i) {
        out << std::setw(2) << std::setfill('0') << dist(rng);
    }
    return out.str();
}

std::string BuildTraceParentFromIds(const std::string& traceId, const std::string& spanId, bool sampled) {
    return "00-" + traceId + "-" + spanId + "-" + (sampled ? "01" : "00");
}

#if AETHER_ENABLE_OTEL
std::string BuildTraceParentFromContext(const opentelemetry::trace::SpanContext& context) {
    if (!context.IsValid()) {
        return BuildTraceParentFromIds(RandomHex(16), RandomHex(8), true);
    }

    return BuildTraceParentFromIds(
        context.trace_id().ToLowerBase16(),
        context.span_id().ToLowerBase16(),
        context.trace_flags().IsSampled());
}
#endif
} // namespace

Tracer& Tracer::Instance() {
    static Tracer instance;
    return instance;
}

void Tracer::Configure(const TraceConfig& config) {
    if (!config.enabled) {
        enabled_ = false;
        return;
    }

#if AETHER_ENABLE_OTEL
    opentelemetry::exporter::otlp::OtlpHttpExporterOptions options;
    if (!config.endpoint.empty()) {
        options.url = config.endpoint;
    }

    auto exporter = std::make_unique<opentelemetry::exporter::otlp::OtlpHttpExporter>(options);
    auto processor = std::make_unique<opentelemetry::sdk::trace::BatchSpanProcessor>(std::move(exporter));
    auto resource = opentelemetry::sdk::resource::Resource::Create(
        {{"service.name", config.serviceName.empty() ? "aether-guard-agent" : config.serviceName}});
    provider_ = std::make_shared<opentelemetry::sdk::trace::TracerProvider>(
        std::move(processor),
        resource);

    opentelemetry::trace::Provider::SetTracerProvider(provider_);
    tracer_ = opentelemetry::trace::Provider::GetTracerProvider()->GetTracer("aether-guard-agent");
    enabled_ = true;
#else
    (void)config;
    enabled_ = false;
#endif
}

bool Tracer::Enabled() const {
    return enabled_;
}

SpanHandle Tracer::StartSpan(const std::string& name) {
    SpanHandle handle;
#if AETHER_ENABLE_OTEL
    if (enabled_ && tracer_) {
        handle.span = tracer_->StartSpan(name);
        handle.traceparent = BuildTraceParentFromContext(handle.span->GetContext());
        handle.valid = true;
        return handle;
    }
#endif

    handle.traceparent = BuildTraceParentFromIds(RandomHex(16), RandomHex(8), true);
    handle.valid = true;
    return handle;
}

void Tracer::SetAttribute(SpanHandle& handle, const std::string& key, const std::string& value) {
#if AETHER_ENABLE_OTEL
    if (enabled_ && handle.span) {
        handle.span->SetAttribute(key, value);
    }
#else
    (void)handle;
    (void)key;
    (void)value;
#endif
}

void Tracer::SetAttribute(SpanHandle& handle, const std::string& key, int64_t value) {
#if AETHER_ENABLE_OTEL
    if (enabled_ && handle.span) {
        handle.span->SetAttribute(key, value);
    }
#else
    (void)handle;
    (void)key;
    (void)value;
#endif
}

void Tracer::EndSpan(SpanHandle& handle, bool success) {
#if AETHER_ENABLE_OTEL
    if (enabled_ && handle.span) {
        handle.span->SetStatus(
            success ? opentelemetry::trace::StatusCode::kOk : opentelemetry::trace::StatusCode::kError);
        handle.span->End();
    }
#else
    (void)handle;
    (void)success;
#endif
}

void Tracer::Shutdown() {
#if AETHER_ENABLE_OTEL
    if (provider_) {
        provider_->Shutdown();
    }
#endif
}
