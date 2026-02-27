# CP3407 TDD Evidence Chain (v2.3)

Last updated: 2026-02-27

This page provides an assessor-facing Test-Driven Development (TDD) evidence chain
for the CP3407 rubric testing criterion ("Test-driven development").

## 1) Evidence Standard

To keep evidence auditable, each feature is classified into one of three levels:

- **Level A - Strict test-first**: failing test added before implementation commit (Red -> Green -> Refactor).
- **Level B - Co-committed test+implementation**: tests and implementation delivered in the same commit/PR for the same feature delta.
- **Level C - Backfill hardening**: tests added after implementation to close coverage gaps.

Target policy from 2026-02-27 onward:

- New feature work must provide **Level A** evidence by default.
- **Level B/C** is allowed only with explicit PR justification and follow-up action.

## 2) v2.3 Evidence Matrix (Auditable)

| Scope / Issue | Test Artifact | Implementation Artifact | Test Commit | Implementation Commit | Evidence Level | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| M0 external signal parsing (#21/#26) | `src/services/core-dotnet/AetherGuard.Core.Tests/ExternalSignalParserTests.cs` | `src/services/core-dotnet/AetherGuard.Core/Services/ExternalSignals/ExternalSignalParser.cs` | `66be642` | `66be642` | B | Parser behavior and parsing rules delivered with tests in same feature commit. |
| M1 enrichment batch integration (#32) | `src/services/core-dotnet/AetherGuard.Core.Tests/ExternalSignalEnrichmentClientTests.cs` | `src/services/core-dotnet/AetherGuard.Core/Services/ExternalSignals/ExternalSignalEnrichmentClient.cs` | `99a5864` | `99a5864` (feature delta) | B | Existing client introduced earlier; batch contract + validation tests delivered with the milestone change. |
| M3 per-agent rollout gating (#41) | `src/services/core-dotnet/AetherGuard.Core.Tests/AgentWorkflowServiceTests.cs` | `src/services/core-dotnet/AetherGuard.Core/Services/AgentWorkflowService.cs` | `e817897` | `e817897` (feature delta) | B | Rollout gating behavior and tests delivered in the same change set. |
| M4 dynamic risk guardrails (#43/#45) | `src/services/core-dotnet/AetherGuard.Core.Tests/DynamicRiskPolicyTests.cs` | `src/services/core-dotnet/AetherGuard.Core/Services/DynamicRiskPolicy.cs` | `112ab1a` | `112ab1a` | B | Guardrail and threshold logic delivered with direct unit tests. |
| Agent CRIU command safety | `src/services/agent-cpp/tests/CriuCommandTests.cpp` | `src/services/agent-cpp/CriuManager.cpp` | `27bc0f8` | `27bc0f8` | B | Command composition and invalid-input behavior verified at delivery time. |
| Agent local inference gates (#40) | `src/services/agent-cpp/tests/InferenceEngineTests.cpp` | `src/services/agent-cpp/InferenceEngine.cpp` | `0783022` | `0783022` | B | Rollout/fallback inference behavior verified in same feature commit. |
| AI risk model rules hardening | `src/services/ai-engine/tests/test_model.py` | `src/services/ai-engine/model.py` | `f8cc49c` | `2e7442d`, `5598031` | C | Post-implementation hardening to improve rubric-aligned automated coverage. |
| Web API normalization hardening | `src/web/dashboard/tests/api.utils.test.ts` | `src/web/dashboard/lib/api.ts` | `f8cc49c` | `babbfcb` | C | Post-implementation hardening for parser/normalization utility behavior. |

Commit links can be resolved as:

- `https://github.com/JasonEran/Aether-Guard/commit/<sha>`

## 3) Validation Evidence

Local replay commands used for current baseline:

```bash
dotnet test src/services/core-dotnet/AetherGuard.Core.Tests/AetherGuard.Core.Tests.csproj -c Release
ctest --test-dir src/services/agent-cpp/build_cp3407_audit -C Release --output-on-failure
python -m unittest discover -s src/services/ai-engine/tests -p "test_*.py"
npm test --prefix src/web/dashboard
```

Expected: all pass on `master` for release baseline evidence.

## 4) Gap Closure Actions (Completed)

- PR template now requires explicit TDD evidence fields (commit IDs + evidence level).
- v2.3 acceptance template now includes mandatory TDD evidence and exception notes.
- Assessor checklist and testing strategy pages now link this TDD chain page.

## 5) Forward Enforcement (for next release)

For each new issue/PR, include:

1. Red test commit SHA.
2. Green implementation commit SHA.
3. Refactor commit SHA (if applicable).
4. Evidence level (`A/B/C`) and justification.
5. If level is `B` or `C`, a follow-up issue to migrate toward level `A`.
