using AetherGuard.Core.Models;

namespace AetherGuard.Core.Services;

public sealed class TelemetryStore
{
    private readonly object _lock = new();
    private TelemetryPayload? _latestTelemetry;
    private AnalysisResult? _latestAnalysis;

    public void Update(TelemetryPayload telemetry, AnalysisResult? analysis)
    {
        if (telemetry is null)
        {
            return;
        }

        lock (_lock)
        {
            _latestTelemetry = telemetry;
            _latestAnalysis = analysis;
        }
    }

    public TelemetrySnapshot? GetLatest()
    {
        lock (_lock)
        {
            if (_latestTelemetry is null)
            {
                return null;
            }

            return new TelemetrySnapshot(_latestTelemetry, _latestAnalysis);
        }
    }
}

public record TelemetrySnapshot(TelemetryPayload Telemetry, AnalysisResult? Analysis);
