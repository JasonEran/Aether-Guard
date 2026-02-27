# CP3407 Stakeholder Feedback Evidence (v2.3)

Last updated: 2026-02-27

This page provides explicit evidence for rubric criterion 3:
"Demonstration of and client feedback on your deployed solution after each iteration".

## Evidence Method

- Feedback entries are captured from milestone review notes and issue threads.
- Each entry is traced as: feedback -> decision -> implemented change -> verification artifact.
- Stakeholder role in this course context: tutor/assessor review perspective and operator-facing product feedback.

## Feedback-to-Change Matrix

| Iteration | Feedback captured | Source evidence | Change implemented | Verification evidence |
| --- | --- | --- | --- | --- |
| M0 (External signals) | Need explicit feed health visibility and retention policy. | `docs/CP3407-Iteration-Log.md` (M0 section), issue scope #21/#25/#26 | Added feed-state APIs, cleanup policy, and normalization for severity/region/tags. | `docs/QA-SmokeTest-v2.3.md`, `docs/Release-Notes-v2.3.md` |
| M1 (Semantic enrichment) | Require fallback path when NLP provider/model is unavailable. | `docs/CP3407-Iteration-Log.md` (M1 section), issue scope #31/#32/#33 and M1 epic update (`https://github.com/JasonEran/Aether-Guard/issues/10`) | Implemented heuristic fallback route and batch-first enrichment path. | `docs/QA-SmokeTest-v2.3-M1.md`, `docs/Release-Notes-v2.3.md` |
| M2 (Fusion/backtesting) | Evidence must be reproducible and versioned for release gating. | `docs/CP3407-Iteration-Log.md` (M2 section), issue scope #34/#35/#36/#37/#38 | Added run manifests, artifact registry/versioning, reproducibility verification workflow. | `docs/AI-Artifact-Versioning-v2.3-M2.md`, `docs/AI-Backtesting-v2.3-M2.md` |
| M3 (Federated inference) | Rollout risk must be controllable per-agent and reversible. | `docs/CP3407-Iteration-Log.md` (M3 section), issue scope #40/#41/#42 | Added per-agent rollout gates, fallback switches, canary evaluator and rollback criteria. | `docs/QA-Canary-Rollback-v2.3-M3.md`, `scripts/qa/evaluate_m3_canary.py` |
| M4 (Dynamic risk) | Decision transparency is required for operator trust. | `docs/CP3407-Iteration-Log.md` (M4 section), issue scope #43/#44/#45 | Exposed `alpha`, `P_preempt`, rationale and top signals in dashboard explainability panel. | `docs/Web-Explainability-v2.3-M4.md`, `src/web/dashboard/app/DashboardClient.tsx` |
| CI/Release hardening | Workflow stability and action version pinning required for reliable release/assessment replay. | `docs/CP3407-Iteration-Log.md` (CI/Release section), issue scope #46/#47/#48 | Added trigger restrictions, pinned action versions, and release checklist gating. | `docs/CI-SupplyChain-Stabilization-v2.3.md`, successful runs + release tag links in `docs/CP3407-Assessor-OneClick.md` |

## Demonstration Evidence

Deployment and demonstration evidence used in milestone reviews:

- Smoke walkthroughs: `docs/QA-SmokeTest-v2.3.md`, `docs/QA-SmokeTest-v2.3-M1.md`
- Canary/rollback replay: `docs/QA-Canary-Rollback-v2.3-M3.md`
- CI proof-of-deployment quality:
  - https://github.com/JasonEran/Aether-Guard/actions/runs/22479358167
  - https://github.com/JasonEran/Aether-Guard/actions/runs/22479358253
- Release artifact:
  - https://github.com/JasonEran/Aether-Guard/releases/tag/v2.3.0

## Traceability Note

This page is a consolidated evidence index for assessor replay. It does not replace raw issue/PR history,
but makes the feedback-to-change chain explicit and quickly auditable.
