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

## Raw Replay Index

This appendix points assessors to the most direct raw evidence sources behind the
feedback summaries above, so the review can move from curated narrative to primary artifacts.

### Iteration and issue scopes

- M0 scope issues:
  - https://github.com/JasonEran/Aether-Guard/issues/20
  - https://github.com/JasonEran/Aether-Guard/issues/21
  - https://github.com/JasonEran/Aether-Guard/issues/23
  - https://github.com/JasonEran/Aether-Guard/issues/25
  - https://github.com/JasonEran/Aether-Guard/issues/26
- M1 epic and scope:
  - https://github.com/JasonEran/Aether-Guard/issues/10
  - https://github.com/JasonEran/Aether-Guard/issues/29
  - https://github.com/JasonEran/Aether-Guard/issues/30
  - https://github.com/JasonEran/Aether-Guard/issues/31
  - https://github.com/JasonEran/Aether-Guard/issues/32
  - https://github.com/JasonEran/Aether-Guard/issues/33
- M2 epic and scope:
  - https://github.com/JasonEran/Aether-Guard/issues/11
  - https://github.com/JasonEran/Aether-Guard/issues/34
  - https://github.com/JasonEran/Aether-Guard/issues/35
  - https://github.com/JasonEran/Aether-Guard/issues/36
  - https://github.com/JasonEran/Aether-Guard/issues/37
  - https://github.com/JasonEran/Aether-Guard/issues/38
- M3 epic and scope:
  - https://github.com/JasonEran/Aether-Guard/issues/12
  - https://github.com/JasonEran/Aether-Guard/issues/39
  - https://github.com/JasonEran/Aether-Guard/issues/40
  - https://github.com/JasonEran/Aether-Guard/issues/41
  - https://github.com/JasonEran/Aether-Guard/issues/42
- M4 epic and scope:
  - https://github.com/JasonEran/Aether-Guard/issues/13
  - https://github.com/JasonEran/Aether-Guard/issues/43
  - https://github.com/JasonEran/Aether-Guard/issues/44
  - https://github.com/JasonEran/Aether-Guard/issues/45
- CI / release hardening:
  - https://github.com/JasonEran/Aether-Guard/issues/46
  - https://github.com/JasonEran/Aether-Guard/issues/47
  - https://github.com/JasonEran/Aether-Guard/issues/48

### Release-track pull requests

- `#49` full delivery / release readiness:
  - https://github.com/JasonEran/Aether-Guard/pull/49
- `#50` CI stabilization:
  - https://github.com/JasonEran/Aether-Guard/pull/50
- `#51` process templates + diagrams:
  - https://github.com/JasonEran/Aether-Guard/pull/51

### Replay path for assessors

1. Start with the milestone summary in `docs/CP3407-Iteration-Log.md`.
2. Open the linked issue scope above to view the raw discussion and closure history.
3. Cross-check the implementation delta in the release-track PRs.
4. Replay the validation artifact from the smoke/canary/release links listed in this page.

## Traceability Note

This page is a consolidated evidence index for assessor replay. It does not replace raw issue/PR history,
but makes the feedback-to-change chain explicit and quickly auditable.
