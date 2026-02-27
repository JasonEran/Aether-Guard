# CP3407 Requirements, Priority, and Estimation (v2.3)

Last updated: 2026-02-27

This page provides an assessor-friendly requirements view aligned to the CP3407 rubric requirement:
correct requirements, justified priorities, and realistic delivery sequencing.

## Planning Method

- Priority model: MoSCoW (`Must`, `Should`, `Could`).
- Estimation model: Story Points (SP) using relative complexity (1, 2, 3, 5, 8).
- Budget proxy: engineering effort tracked as SP and milestone scope.
- Delivery sequencing: issue dependencies grouped by milestones M0-M4.

## User Story Matrix (Release Scope)

| Story / Issue | User Value | Priority | Estimate (SP) | Iteration |
| --- | --- | --- | --- | --- |
| #20 External signals ingestion + persistence | Platform can ingest and store provider incident signals | Must | 5 | M0 |
| #21 Feed health tracking + API | Operators can verify feed freshness and source health | Must | 3 | M0 |
| #23 Dashboard signals panel | Users can see ingested signals in GUI | Must | 3 | M0 |
| #25 Signals retention + cleanup | Storage remains stable and compliant over time | Must | 2 | M0 |
| #26 Severity/region/tag normalization | Downstream analytics receive consistent data | Must | 3 | M0 |
| #29 Versioned `S_v/P_v/B_s` schema + OpenAPI | Semantic outputs remain compatible and verifiable | Must | 3 | M1 |
| #30 Real NLP sentiment batch enrichment | Better semantic quality and throughput | Must | 8 | M1 |
| #31 Summarizer + caching | Better signal readability and lower repeated inference cost | Should | 5 | M1 |
| #32 Fetch -> enrich -> persist pipeline | End-to-end semantic enrichment in control plane | Must | 8 | M1 |
| #33 Enrichment metrics/traces + dashboards | Operators can observe latency/error behavior | Must | 5 | M1 |
| #34 Data acquisition scripts/provenance | Offline training data is reproducible and explainable | Must | 5 | M2 |
| #35 TSMixer baseline + ONNX export | Deployable forecasting baseline for agent inference | Must | 8 | M2 |
| #36 Fusion model with exogenous vectors | Improve preemption prediction under external volatility | Must | 8 | M2 |
| #37 Backtesting harness vs v2.2 | Quantify benefit before online rollout | Must | 5 | M2 |
| #38 Artifact versioning + reproducibility | Release-safe model lineage and replayability | Must | 3 | M2 |
| #39 Heartbeat semantic payload extension | Agent receives online semantic features | Must | 3 | M3 |
| #40 Agent local ONNX inference + gating | Low-latency local inference and controlled rollout | Must | 8 | M3 |
| #41 Core semantic push + fallback | Online delivery with compatibility safety | Must | 5 | M3 |
| #42 Canary + rollback evaluator | Safe promotion/rollback mechanism | Must | 5 | M3 |
| #43 Dynamic alpha + guardrails | Adaptive risk allocation with safety constraints | Must | 8 | M4 |
| #44 Dashboard explainability (`alpha`, `P_preempt`) | Decisions become transparent to users | Must | 5 | M4 |
| #45 Guardrail regression tests | Prevent dynamic-risk regressions | Must | 3 | M4 |
| #46 Trigger restrictions | Avoid wasteful CI runs and reduce risk | Should | 2 | CI |
| #47 Supply-chain workflow fixes | Keep provenance/signing pipeline operational | Must | 3 | CI |
| #48 Release checklist | Standardize release acceptance quality | Must | 2 | Release |

## Priority Justification

- **Must**: core platform behavior, correctness, safety, compatibility, and release criteria.
- **Should**: non-blocking improvements that increase operability and confidence.
- **Could**: deferred enhancements not required for v2.3 release readiness.

## Delivery Outcome

- Total planned scope (listed above): 118 SP.
- Delivery result: all listed release-scope issues closed and tagged in `v2.3.0`.
- Sequencing matched dependency order (M0 -> M1 -> M2 -> M3 -> M4 -> CI/release).

## Budget / Time-on-Budget Statement

- The release track used milestone-gated scope to avoid uncontrolled expansion.
- Each milestone closed only after acceptance evidence (tests/docs/observability) was attached.
- CI and release criteria were treated as first-class scope (issues #46, #47, #48), not post-hoc work.
- Detailed numeric budget/schedule table is published in `docs/CP3407-Budget-Tracking-v2.3.md`.
