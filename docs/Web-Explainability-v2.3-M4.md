# v2.3 M4 Dashboard Explainability

Issue: #44  
Epic: #13

## Goal

Expose dynamic-risk explainability to operators in the dashboard:

- `alpha`
- `P_preempt`
- top fused signals
- decision rationale + confidence

## API Surface

`GET /api/v1/dashboard/latest` now returns extra fields under `analysis`:

- `alpha`
- `preemptProbability`
- `decisionScore`
- `rationale`
- `topSignals[]` (`key`, `label`, `value`, `source`, `detail`)

`DashboardAnalysis` in `src/shared/protos/control_plane.proto` is updated with additive fields to keep gRPC/JSON-transcoding parity.

## Explainability Derivation

Core computes explainability with existing dynamic-risk policy math:

- `P_preempt` from AI analysis confidence/prediction + rebalance overrides.
- `alpha` from dynamic policy (`volatility`, `sentiment pressure`, rebalance force-to-max path).
- `decisionScore = clamp(P_preempt * alpha, 0, 1)`.
- `topSignals` ranked from fused inputs (telemetry + AI + enriched external signal semantics).

When no enriched external signal exists, semantic values use safe fallback defaults and indicate fallback source.

## Dashboard UI

`Explainability` panel now shows:

- AI status, confidence, predicted CPU
- `alpha`, `P_preempt`, decision score
- top 3 fused signals with source/detail
- decision rationale
- root cause

## Local Validation

- `dotnet build src/services/core-dotnet/AetherGuard.Core/AetherGuard.Core.csproj -c Release`
- `dotnet test src/services/core-dotnet/AetherGuard.Core.Tests/AetherGuard.Core.Tests.csproj -c Release`
- `npm run lint` (in `src/web/dashboard`)
- `npm run build` (in `src/web/dashboard`)

All commands passed.
