# CP3407 Testing Strategy and Evidence (v2.3)

Last updated: 2026-02-27

This page is the rubric-facing testing explanation for CP3407 criterion 4
("exemplary testing of all components" with acceptance evidence).

## 1) Test Scope by Layer

| Layer | Goal | Evidence |
| --- | --- | --- |
| Unit tests | Validate core logic deterministically | Core: `src/services/core-dotnet/AetherGuard.Core.Tests/*` |
| Service/component tests | Validate service-specific behavior and contracts | Agent C++ tests: `src/services/agent-cpp/tests/*`; AI model logic: `src/services/ai-engine/tests/test_model.py`; Web data-flow + route contract tests: `src/web/dashboard/tests/api.utils.test.ts`, `src/web/dashboard/tests/dashboard.routes.test.ts` |
| Acceptance/smoke | Verify end-to-end operator workflows | `docs/QA-SmokeTest-v2.3.md`, `docs/QA-SmokeTest-v2.3-M1.md` |
| Rollout safety | Validate canary and rollback behavior | `docs/QA-Canary-Rollback-v2.3-M3.md` |
| CI quality gates | Prevent regressions on merge | `.github/workflows/quality-gate.yml` |

## 2) What Is Covered

- **Core (.NET)**:
  - external signal parsing and enrichment client behavior
  - dynamic risk policy correctness and guardrail behavior
  - options normalization and below-threshold decision handling
  - agent workflow orchestration paths
- **Agent (C++)**:
  - CRIU command invocation and fallback handling
  - local inference engine test coverage
- **AI (Python/FastAPI)**:
  - model logic paths in `model.py` via `unittest`
- **Web (Next.js/TypeScript)**:
  - API utility normalization/parsing helpers for dashboard data handling
  - data-to-UI mapping for fleet status and risk history
  - route contract behavior for proxy endpoints (`latest`/`history`)

## 3) CI Execution Standard

The quality gate runs on `master` and release branches with:

- .NET restore/build/test
- C++ configure/build/test
- Web install/lint/test/build
- AI syntax checks and unit tests

This creates a single merge gate that aligns implementation with test evidence.

## 4) Acceptance Traceability

- Milestone test evidence is linked from:
  - `docs/Release-Notes-v2.3.md`
  - `docs/CP3407-Iteration-Log.md`
  - `docs/CP3407-Assessor-OneClick.md`
- For assessor replay, use:
  - smoke + canary documents above
  - latest passing quality-gate and supply-chain runs

## 5) Requirements-to-Test Traceability

| Requirement / Issue Group | Primary automated checks | Acceptance / replay evidence |
| --- | --- | --- |
| M0 external signals ingestion, normalization, retention (`#20 #21 #23 #25 #26`) | `ExternalSignalParserTests.cs`; Web route tests for dashboard proxy behavior | `docs/QA-SmokeTest-v2.3.md` |
| M1 semantic enrichment + fallback (`#29 #30 #31 #32 #33`) | `ExternalSignalEnrichmentClientTests.cs`; AI `test_model.py` fallback-oriented cases | `docs/QA-SmokeTest-v2.3-M1.md` |
| M2 reproducible fusion/backtesting (`#34 #35 #36 #37 #38`) | AI unit tests; `scripts/qa/verify_tdd_evidence.py` ledger validation | `docs/AI-Backtesting-v2.3-M2.md`, `docs/AI-Artifact-Versioning-v2.3-M2.md` |
| M3 online inference rollout and rollback (`#39 #40 #41 #42`) | `AgentWorkflowServiceTests.cs`; `InferenceEngineTests.cpp`; `CriuCommandTests.cpp` | `docs/QA-Canary-Rollback-v2.3-M3.md` |
| M4 dynamic risk + explainability (`#43 #44 #45`) | `DynamicRiskPolicyTests.cs`; `api.utils.test.ts` dashboard mapping and normalization tests | `docs/Core-Dynamic-Risk-v2.3-M4.md`, `docs/Web-Explainability-v2.3-M4.md` |
| CI / release readiness (`#46 #47 #48`) | `.github/workflows/quality-gate.yml`; TDD ledger verification | `docs/Release-Notes-v2.3.md`, `docs/CP3407-Assessor-OneClick.md` |

This matrix is intended to make rubric criterion 4 auditable: each release-scope
requirement has a direct path from implementation to automated check to assessor replay artifact.

## 6) Automated Execution Snapshot

Representative local replay commands for the current baseline:

```bash
dotnet test src/services/core-dotnet/AetherGuard.Core.Tests/AetherGuard.Core.Tests.csproj -c Release --no-restore
ctest --test-dir src/services/agent-cpp/build_cp3407_audit -C Release --output-on-failure
python -m unittest discover -s src/services/ai-engine/tests -p "test_*.py"
npm test --prefix src/web/dashboard
```

Expected baseline:

- .NET tests pass, including dynamic-risk guardrail and normalization cases.
- C++ tests pass for CRIU command safety and inference fallback gates.
- AI tests pass for stable, spike, volatility, and rebalance-driven risk paths.
- Web tests pass for parsing helpers, dashboard data mapping, and route contracts.

## 7) TDD Evidence Chain

- TDD evidence ledger (commit-level traceability):
  - `docs/CP3407-TDD-Evidence-v2.3.md`
- Latest machine-verification report:
  - `docs/CP3407-TDD-Verification-v2.3.md`
- Evidence levels are explicitly classified as:
  - `A` strict test-first
  - `B` co-committed test + implementation
  - `C` post-implementation backfill
- Policy from 2026-02-27 onward:
  - New feature work defaults to level `A`; `B/C` requires explicit PR justification.

