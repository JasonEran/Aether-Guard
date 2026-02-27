# CP3407 Budget and Schedule Tracking (v2.3)

Last updated: 2026-02-27

This page provides numeric evidence for rubric criteria requiring delivery
"on time and on budget".

## 1) Budget Model (Project Context)

For this software project, budget is tracked using two quantitative controls:

- Scope budget: story points (SP) committed for release scope.
- Schedule budget: milestone target dates vs actual completion dates.

Primary planning source:

- `docs/CP3407-Requirements-v2.3.md`

Milestone target dates source:

- `docs/CP3407-Iteration-Log.md`

Actual completion source:

- GitHub issue closure timestamps for release-scope issues (#20-#48).

## 2) Scope Budget Table (Planned vs Delivered)

| Milestone | Issue set | Planned SP | Delivered SP | Variance (SP) | Status |
| --- | --- | ---: | ---: | ---: | --- |
| M0 | #20 #21 #23 #25 #26 | 16 | 16 | 0 | On budget |
| M1 | #29 #30 #31 #32 #33 | 29 | 29 | 0 | On budget |
| M2 | #34 #35 #36 #37 #38 | 29 | 29 | 0 | On budget |
| M3 | #39 #40 #41 #42 | 21 | 21 | 0 | On budget |
| M4 | #43 #44 #45 | 16 | 16 | 0 | On budget |
| CI | #46 #47 | 5 | 5 | 0 | On budget |
| Release | #48 | 2 | 2 | 0 | On budget |
| **Total** | #20-#48 | **118** | **118** | **0** | **On budget** |

## 3) Schedule Budget Table (Target vs Actual)

| Milestone | Planned completion | Actual completion | Variance (days) | Notes |
| --- | --- | --- | ---: | --- |
| M0 | 2026-02-11 | 2026-02-11 | 0 | Closed by #25/#26 completion date |
| M1 | 2026-02-19 | 2026-02-22 | +3 | Observability hardening (#33) extended milestone end |
| M2 | 2026-02-25 | 2026-02-25 | 0 | Closed by #38 completion date |
| M3 | 2026-02-25 | 2026-02-25 | 0 | Closed by #42 completion date |
| M4 | 2026-02-25 | 2026-02-25 | 0 | Closed by #43/#44/#45 completion date |
| CI hardening | 2026-02-27 | 2026-02-25 | -2 | #46/#47 closed early; final CI evidence on 2026-02-27 |
| Release publish | 2026-02-26 | 2026-02-26 | 0 | `v2.3.0` release published |

## 4) Numeric Delivery Summary

- Scope budget variance: `0 / 118 SP` (`0%`).
- Milestones delivered within target except M1 (`+3 days`), recovered by M2-M4 with no further slippage.
- Release publication met target date (`2026-02-26`).

## 5) Cost Footprint (Demo/Assessment Environment)

- Local assessment stack uses Docker Compose and local resources (no mandatory cloud spend).
- Operational proof relies on CI artifacts/runs and release assets rather than paid production infrastructure.

## 6) Evidence Links

- Planning and SP matrix: `docs/CP3407-Requirements-v2.3.md`
- Iteration targets and closure narrative: `docs/CP3407-Iteration-Log.md`
- Release publication date: `docs/Release-Notes-v2.3.md`
- Assessor quick navigation: `docs/CP3407-Assessor-OneClick.md`
