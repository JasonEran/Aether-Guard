namespace AetherGuard.Core.Services.SchemaRegistry;

public static class TelemetrySchemaDefinitions
{
    public const string TelemetryEnvelopeSubject = "telemetry-envelope";
    public const string TelemetryPayloadSubject = "telemetry-payload";
    public const int TelemetryEnvelopeVersion = 1;
    public const int TelemetryPayloadVersion = 1;

    public const string TelemetryEnvelopeV1 = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "title": "TelemetryEnvelope",
          "type": "object",
          "required": ["schemaVersion", "sentAt", "payload"],
          "properties": {
            "schemaVersion": { "type": "integer", "minimum": 1 },
            "sentAt": { "type": "integer", "minimum": 0 },
            "payload": {
              "type": "object",
              "required": ["agentId", "timestamp", "workloadTier", "rebalanceSignal", "diskAvailable"],
              "properties": {
                "agentId": { "type": "string", "minLength": 1 },
                "timestamp": { "type": "integer", "minimum": 0 },
                "workloadTier": { "type": "string", "minLength": 1 },
                "rebalanceSignal": { "type": "boolean" },
                "diskAvailable": { "type": "integer", "minimum": 0 }
              }
            }
          }
        }
        """;

    public const string TelemetryPayloadV1 = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "title": "TelemetryPayload",
          "type": "object",
          "required": ["agentId", "timestamp", "workloadTier", "rebalanceSignal", "diskAvailable"],
          "properties": {
            "agentId": { "type": "string", "minLength": 1 },
            "timestamp": { "type": "integer", "minimum": 0 },
            "workloadTier": { "type": "string", "minLength": 1 },
            "rebalanceSignal": { "type": "boolean" },
            "diskAvailable": { "type": "integer", "minimum": 0 }
          }
        }
        """;
}
