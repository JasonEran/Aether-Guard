# v2.3 M3 Heartbeat Semantic Payload Contract

This document captures issue #39: extending heartbeat payloads with semantic features while keeping backward compatibility.

## Scope

- Protobuf contract update: `src/shared/protos/agent_service.proto`
- Core heartbeat response wiring:
  - gRPC: `AgentWorkflowService`
  - legacy REST bridge: `AgentController`

## Protobuf Additions

New message:

- `SemanticHeartbeatFeatures`
  - `schema_version`
  - `s_v_negative`
  - `s_v_neutral`
  - `s_v_positive`
  - `p_v`
  - `b_s`
  - `source`
  - `generated_at_unix`
  - `fallback_used`

`HeartbeatResponse` additive field:

- `semantic_features = 3`

No existing field numbers were changed, so wire compatibility is preserved.

## JSON Transcoding Shape

`POST /api/v2/agent/heartbeat` response now includes:

```json
{
  "status": "active",
  "commands": [],
  "semanticFeatures": {
    "schemaVersion": "1.0",
    "sVNegative": 0.33,
    "sVNeutral": 0.34,
    "sVPositive": 0.33,
    "pV": 0.5,
    "bS": 0.5,
    "source": "fallback:neutral",
    "generatedAtUnix": 1767000000,
    "fallbackUsed": true
  }
}
```

Older agents that ignore unknown JSON/protobuf fields remain compatible.

## Runtime Source Selection

Core heartbeat selects the latest enriched external signal vector when available.  
If no enriched signal is available, it emits a neutral fallback vector and marks `fallback_used=true`.
