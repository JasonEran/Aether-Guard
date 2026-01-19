using AetherGuard.Core.Models;

namespace AetherGuard.Core.Services;

public sealed class TelemetryStore
{
    private readonly object _lock = new();
    private TelemetryPayload? _latest;

    public void Update(TelemetryPayload data)
    {
        if (data is null)
        {
            return;
        }

        lock (_lock)
        {
            _latest = data;
        }
    }

    public TelemetryPayload? GetLatest()
    {
        lock (_lock)
        {
            return _latest;
        }
    }
}
