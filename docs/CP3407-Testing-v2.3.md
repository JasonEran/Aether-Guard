# CP3407 Testing Strategy and Evidence (v2.3)

Last updated: 2026-02-27

This page is the rubric-facing testing explanation for CP3407 criterion 4
("exemplary testing of all components" with acceptance evidence).

## 1) Test Scope by Layer

| Layer | Goal | Evidence |
| --- | --- | --- |
| Unit tests | Validate core logic deterministically | Core: `src/services/core-dotnet/AetherGuard.Core.Tests/*` |
| Service/component tests | Validate service-specific behavior and contracts | Agent C++ tests: `src/services/agent-cpp/tests/*`; AI model logic: `src/services/ai-engine/tests/test_model.py`; Web API helpers: `src/web/dashboard/tests/api.utils.test.ts` |
| Acceptance/smoke | Verify end-to-end operator workflows | `docs/QA-SmokeTest-v2.3.md`, `docs/QA-SmokeTest-v2.3-M1.md` |
| Rollout safety | Validate canary and rollback behavior | `docs/QA-Canary-Rollback-v2.3-M3.md` |
| CI quality gates | Prevent regressions on merge | `.github/workflows/quality-gate.yml` |

## 2) What Is Covered

- **Core (.NET)**:
  - external signal parsing and enrichment client behavior
  - dynamic risk policy correctness and guardrail behavior
  - agent workflow orchestration paths
- **Agent (C++)**:
  - CRIU command invocation and fallback handling
  - local inference engine test coverage
- **AI (Python/FastAPI)**:
  - model logic paths in `model.py` via `unittest`
- **Web (Next.js/TypeScript)**:
  - API utility normalization/parsing helpers for dashboard data handling

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
