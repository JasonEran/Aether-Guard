# PR Template - v2.3 Acceptance Checklist

Copy/paste this into your PR description for v2.3 release gating.

## Scope

- Release track: `v2.3`
- Type: `feature / fix / docs / release`
- Related issues: closes #___

## What Changed

- [ ] Core (.NET)
- [ ] Agent (C++)
- [ ] AI engine (Python)
- [ ] Dashboard (Next.js)
- [ ] Docs / runbooks

Summary:

## Acceptance Checklist (One-Click Gate)

### M1 Semantic Enrichment

- [ ] External signals ingestion + enrichment path is operational
- [ ] Enrichment outputs are schema-versioned and persisted
- [ ] Observability evidence attached (metrics/traces/dashboard)

### M2 Fusion + Backtesting

- [ ] Fusion/backtesting scripts run successfully
- [ ] Artifact versioning + reproducibility evidence attached

### M3 Federated Inference

- [ ] Heartbeat semantic payload is compatible (additive contract)
- [ ] Agent local inference gate + rollback gate validated
- [ ] Canary evaluator evidence attached

### M4 Dynamic Risk + Explainability

- [ ] Dynamic alpha + guardrails enabled and tested
- [ ] Dashboard shows `alpha`, `P_preempt`, top signals, rationale/confidence

### Compatibility / Safety

- [ ] v2.2 compatibility path verified (fallback works)
- [ ] Rollback path verified
- [ ] No breaking API changes without compatibility shim
- [ ] Security checks passed (auth/mTLS/supply-chain impact reviewed)

## Validation Commands

- [ ] `dotnet build src/services/core-dotnet/AetherGuard.Core/AetherGuard.Core.csproj -c Release`
- [ ] `dotnet test src/services/core-dotnet/AetherGuard.Core.Tests/AetherGuard.Core.Tests.csproj -c Release`
- [ ] `cmake --build src/services/agent-cpp/build_m3_inference --config Release --target AetherAgent AetherAgentTests AetherAgentInferenceTests`
- [ ] `ctest --test-dir src/services/agent-cpp/build_m3_inference -C Release --output-on-failure`
- [ ] `npm run lint` (in `src/web/dashboard`)
- [ ] `npm run build` (in `src/web/dashboard`)

Paste key output snippets:

```text
# build/test summary here
```

## Evidence Links

- Jaeger trace / observability screenshots:
- Canary evaluator report:
- Dashboard explainability screenshot:
- Issue comments with final evidence:

## Risk / Rollback

- Risk level: `low / medium / high`
- Rollback command/runbook:
- Guardrail impact notes:
