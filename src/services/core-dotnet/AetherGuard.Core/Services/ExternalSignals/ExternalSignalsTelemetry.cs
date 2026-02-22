using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AetherGuard.Core.Services.ExternalSignals;

public static class ExternalSignalsTelemetry
{
    public const string ActivitySourceName = "AetherGuard.Core.ExternalSignals";
    public const string MeterName = "AetherGuard.Core.ExternalSignals";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> ClientRequestCounter = Meter.CreateCounter<long>(
        "aetherguard.external_signals.client.requests",
        description: "Number of external-signal enrichment client requests.");
    private static readonly Counter<long> ClientFailureCounter = Meter.CreateCounter<long>(
        "aetherguard.external_signals.client.failures",
        description: "Number of external-signal enrichment client failures.");
    private static readonly Histogram<double> ClientLatencyHistogram = Meter.CreateHistogram<double>(
        "aetherguard.external_signals.client.duration.ms",
        unit: "ms",
        description: "Latency of external-signal enrichment client requests.");
    private static readonly Histogram<long> ClientDocumentHistogram = Meter.CreateHistogram<long>(
        "aetherguard.external_signals.client.documents",
        unit: "documents",
        description: "Number of documents sent per client request.");
    private static readonly Counter<long> PipelineRunCounter = Meter.CreateCounter<long>(
        "aetherguard.external_signals.pipeline.runs",
        description: "Number of enrichment pipeline runs.");
    private static readonly Counter<long> PipelineFallbackCounter = Meter.CreateCounter<long>(
        "aetherguard.external_signals.pipeline.fallbacks",
        description: "Number of fallback executions in enrichment pipeline.");
    private static readonly Histogram<double> PipelineLatencyHistogram = Meter.CreateHistogram<double>(
        "aetherguard.external_signals.pipeline.duration.ms",
        unit: "ms",
        description: "Latency of enrichment pipeline runs.");
    private static readonly Histogram<long> PipelineBatchHistogram = Meter.CreateHistogram<long>(
        "aetherguard.external_signals.pipeline.batch.size",
        unit: "signals",
        description: "Batch sizes processed by enrichment pipeline.");
    private static readonly Counter<long> PipelineUpdateCounter = Meter.CreateCounter<long>(
        "aetherguard.external_signals.pipeline.updates",
        description: "Number of persisted enrichment updates.");

    public static void RecordClientRequest(
        string operation,
        int documentCount,
        string outcome,
        double durationMs,
        int? statusCode = null)
    {
        var tags = CreateClientTags(operation, outcome, statusCode);
        ClientRequestCounter.Add(1, tags);
        ClientLatencyHistogram.Record(durationMs, tags);
        ClientDocumentHistogram.Record(Math.Max(0, documentCount), tags);

        if (!string.Equals(outcome, "success", StringComparison.OrdinalIgnoreCase))
        {
            ClientFailureCounter.Add(1, tags);
        }
    }

    public static void RecordPipelineRun(
        string source,
        int batchSize,
        string mode,
        string outcome,
        double durationMs)
    {
        var tags = CreatePipelineTags(source, mode, outcome);
        PipelineRunCounter.Add(1, tags);
        PipelineLatencyHistogram.Record(durationMs, tags);
        PipelineBatchHistogram.Record(Math.Max(0, batchSize), tags);
    }

    public static void RecordPipelineFallback(string source, int batchSize, string reason)
    {
        var tags = CreatePipelineTags(source, "fallback", reason);
        PipelineFallbackCounter.Add(1, tags);
        PipelineBatchHistogram.Record(Math.Max(0, batchSize), tags);
    }

    public static void RecordPipelineUpdates(string source, int summaryUpdates, int vectorUpdates)
    {
        var summaryTags = CreateUpdateTags(source, "summary");
        var vectorTags = CreateUpdateTags(source, "vector");

        if (summaryUpdates > 0)
        {
            PipelineUpdateCounter.Add(summaryUpdates, summaryTags);
        }

        if (vectorUpdates > 0)
        {
            PipelineUpdateCounter.Add(vectorUpdates, vectorTags);
        }
    }

    private static TagList CreateClientTags(string operation, string outcome, int? statusCode)
    {
        var tags = new TagList
        {
            { "operation", operation },
            { "outcome", outcome }
        };

        if (statusCode.HasValue)
        {
            tags.Add("http.status_code", statusCode.Value);
        }

        return tags;
    }

    private static TagList CreatePipelineTags(string source, string mode, string outcome)
    {
        return new TagList
        {
            { "source", source },
            { "mode", mode },
            { "outcome", outcome }
        };
    }

    private static TagList CreateUpdateTags(string source, string updateType)
    {
        return new TagList
        {
            { "source", source },
            { "update_type", updateType }
        };
    }
}
