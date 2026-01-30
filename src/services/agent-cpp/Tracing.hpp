#pragma once

#include <string>

#if AETHER_ENABLE_OTEL
#include <opentelemetry/nostd/shared_ptr.h>
#include <opentelemetry/trace/tracer.h>
#include <opentelemetry/sdk/trace/tracer_provider.h>
#endif

struct TraceConfig {
    bool enabled = false;
    std::string endpoint;
    std::string serviceName;
};

struct SpanHandle {
    std::string traceparent;
    bool valid = false;
#if AETHER_ENABLE_OTEL
    opentelemetry::nostd::shared_ptr<opentelemetry::trace::Span> span;
#endif
};

class Tracer {
public:
    static Tracer& Instance();

    void Configure(const TraceConfig& config);
    bool Enabled() const;

    SpanHandle StartSpan(const std::string& name);
    void SetAttribute(SpanHandle& handle, const std::string& key, const std::string& value);
    void SetAttribute(SpanHandle& handle, const std::string& key, int64_t value);
    void EndSpan(SpanHandle& handle, bool success);
    void Shutdown();

private:
    Tracer() = default;

    bool enabled_ = false;
#if AETHER_ENABLE_OTEL
    std::shared_ptr<opentelemetry::sdk::trace::TracerProvider> provider_;
    opentelemetry::nostd::shared_ptr<opentelemetry::trace::Tracer> tracer_;
#endif
};
