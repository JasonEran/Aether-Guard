# Aether-Guard v2.3 Release Notes

Release date: 2026-02-25  
Release branch: `feature/v2.3`

## Release Summary

v2.3 upgrades Aether-Guard from static/reactive risk handling to a predictive multimodal flow:

- external cloud incident signals are ingested and semantically enriched
- offline fusion/backtesting + reproducible model artifacts are in place
- online semantic delivery and optional agent local ONNX inference are enabled behind gates
- dynamic risk allocation + guardrails drive migration decisions
- dashboard explainability now exposes `alpha`, `P_preempt`, decision score, and top fused signals

## Milestone Closure

### M1 - Semantic Enrichment

- `#33` [Obs] enrichment metrics/traces + dashboards (closed 2026-02-22)
- `#34` [Data] acquisition scripts for spot history/traces/incidents (closed 2026-02-22)

### M2 - Fusion + Backtesting (Offline)

- `#38` [Ops] model artifact versioning + reproducible runs (closed 2026-02-25)
- Epic `#11` closed (2026-02-25)

### M3 - Federated Inference (Online)

- `#39` heartbeat semantic payload extension
- `#40` agent local ONNX inference + feature gating
- `#41` core semantic push + safe fallback + per-agent rollout
- `#42` canary + rollback plan and evaluator
- Epic `#12` closed (2026-02-25)

### M4 - Dynamic Risk Management

- `#43` dynamic risk alpha + guardrails
- `#45` guardrail regression tests
- `#44` web explainability (`alpha`, `P_preempt`, top signals)
- Epic `#13` closed (2026-02-25)

## Validation Evidence (Local)

- Core build/test:
  - `dotnet build src/services/core-dotnet/AetherGuard.Core/AetherGuard.Core.csproj -c Release`
  - `dotnet test src/services/core-dotnet/AetherGuard.Core.Tests/AetherGuard.Core.Tests.csproj -c Release`
- Agent build/test (M3 path):
  - `cmake -S src/services/agent-cpp -B src/services/agent-cpp/build_m3_inference -DAETHER_ENABLE_GRPC=OFF -DAETHER_USE_LOCAL_PROTOBUF=ON -DAETHER_ENABLE_ONNX_RUNTIME=OFF`
  - `cmake --build src/services/agent-cpp/build_m3_inference --config Release --target AetherAgent AetherAgentTests AetherAgentInferenceTests`
  - `ctest --test-dir src/services/agent-cpp/build_m3_inference -C Release --output-on-failure`
- Dashboard build checks:
  - `npm run lint` (in `src/web/dashboard`)
  - `npm run build` (in `src/web/dashboard`)
- Canary evaluator:
  - `python scripts/qa/evaluate_m3_canary.py ...` (promote/rollback sample evidence under `.tmp/`)

## Key Documentation

- v2.3 roadmap: `docs/ROADMAP-v2.3.md`
- dynamic risk (core): `docs/Core-Dynamic-Risk-v2.3-M4.md`
- web explainability (M4): `docs/Web-Explainability-v2.3-M4.md`
- canary + rollback (M3): `docs/QA-Canary-Rollback-v2.3-M3.md`

## Known Follow-ups

- `#8` course/project management epic (open)
