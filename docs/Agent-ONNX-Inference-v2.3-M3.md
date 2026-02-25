# v2.3 M3 Agent Local ONNX Inference + Feature Gating

This document captures issue #40.

## Scope

- Agent local inference runtime integration points.
- Feature gate and rollback switches for safe rollout.

## Implementation

### New agent components

- `src/services/agent-cpp/InferenceEngine.hpp`
- `src/services/agent-cpp/InferenceEngine.cpp`
- `src/services/agent-cpp/SemanticFeatures.hpp`

### Heartbeat semantic payload consumption

- `NetworkClient::SendHeartbeat(...)` now parses `semanticFeatures` from heartbeat response.
- Parsed fields include:
  - `schemaVersion`
  - `sVNegative`, `sVNeutral`, `sVPositive`
  - `pV`, `bS`
  - `source`, `generatedAtUnix`, `fallbackUsed`

### ONNX runtime integration mode

- Build switch: `AETHER_ENABLE_ONNX_RUNTIME`
- CMake input when enabled:
  - `ONNXRUNTIME_ROOT` (must contain `include/` and `lib/`)
- Runtime model path:
  - `AG_ONNX_MODEL_PATH`

If ONNX runtime is unavailable (or model path invalid), the engine can fail-open to fallback scoring.

## Feature Gate + Rollback

- `AG_M3_ONLINE_INFERENCE_ENABLED`  
  Enables local inference path.
- `AG_M3_FORCE_V22_FALLBACK`  
  Forces rollback behavior and bypasses ONNX usage.
- `AG_ONNX_FAIL_OPEN`  
  If `true`, agent continues with fallback scoring when ONNX init/runtime fails.
- `AG_ONNX_DECISION_THRESHOLD`  
  Decision threshold for preemption recommendation.

## Validation

- Core heartbeat contract remains compatible (semantic fields are additive).
- Agent fallback path is covered by `AetherAgentInferenceTests`.
