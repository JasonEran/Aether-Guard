# CP3407 HD Evidence Matrix (Aether-Guard v2.3)

Last updated: 2026-02-27

This page maps the repository evidence to:

- `CP3407_ProjectRubric.docx`
- `CP3407_Projects.docx`

Goal: demonstrate HD-level readiness and identify remaining gaps to exceed HD.

## A. CP3407 Projects Compliance (Hard Requirements)

| Requirement | Evidence | Status |
| --- | --- | --- |
| Software/IT development project with source code | Multi-service codebase across C++, .NET, Python, and Next.js (`src/services/**`, `src/web/**`) | Met |
| Modern database required | TimescaleDB/PostgreSQL in compose (`docker-compose.yml`) | Met |
| Modern GUI required | Next.js dashboard with authenticated UI and API routes (`src/web/dashboard/**`) | Met |
| Use modern tools/libraries encouraged for HD | .NET 8, FastAPI, Next.js 16, Docker, OpenTelemetry, CI supply-chain signing (`README.md`, `.github/workflows/**`) | Met |

## B. Rubric Mapping (HD Target)

### 1) Requirements

Current evidence:

- v2.3 milestones and scope are explicitly defined with exit criteria (`docs/ROADMAP-v2.3.md`).
- Work is decomposed into epics/issues with completion status (GitHub issues #9-#48 all closed).

Gap to secure HD+:

- Add explicit user-story estimation + priority + budget/effort justification table in a dedicated planning page.

### 2) Design

Current evidence:

- Architecture rationale and end-to-end flow (`docs/ARCHITECTURE-v2.3.md`).
- UML-style class diagrams for Core/Agent/AI/Web (`docs/diagrams/Class-Diagram-v2.3.md`).

Gap to secure HD+:

- Add external-tool artifacts expected by rubric (UML tool export, DB ERD export, UI prototype export links/screenshots).

### 3) Implementation / Code

Current evidence:

- v2.3 release delivered and tagged (`v2.3.0`).
- Milestone closure and implementation references (`docs/Release-Notes-v2.3.md`).
- Merged release-track PRs (#49, #50, #51).

Status: strong for HD.

### 4) Test

Current evidence:

- Core unit tests (`src/services/core-dotnet/AetherGuard.Core.Tests/**`).
- Agent tests (`src/services/agent-cpp/tests/**`).
- Smoke/canary checklists (`docs/QA-SmokeTest-v2.3.md`, `docs/QA-SmokeTest-v2.3-M1.md`, `docs/QA-Canary-Rollback-v2.3-M3.md`).
- New quality gate workflow (`.github/workflows/quality-gate.yml`) for build/test/lint automation.

Gap to secure HD+:

- Add AI and Web automated test suites (not only syntax/lint/build).

### 5) Version Control

Current evidence:

- Structured issue templates and PR template (`.github/ISSUE_TEMPLATE/**`, `.github/PULL_REQUEST_TEMPLATE.md`).
- Traceable issue-to-PR-to-release chain across v2.3 epics.

Status: HD-ready.

### 6) Building & Development Tools

Current evidence:

- Build and packaging across .NET/C++/Python/Node + Docker (`README.md`, `docker-compose.yml`).
- CI quality gate + supply-chain workflows (`.github/workflows/quality-gate.yml`, `.github/workflows/supply-chain.yml`).
- SBOM/signing/provenance integration (supply-chain workflow + release documentation).

Gap to secure HD+:

- Add a single "toolchain rationale" page explicitly explaining why each tool was chosen and how it was used.

### 7) Agile Software Engineering

Current evidence:

- Epic/milestone-driven incremental delivery across M0-M4 (`docs/ROADMAP-v2.3.md` + issues).
- Acceptance-oriented PR template (`docs/PR-Template-v2.3-Acceptance.md`).

Gap to secure HD+:

- Add iteration evidence page (iteration goals, demo outcomes, feedback, retrospective actions).

### 8) Project Technical Writing

Current evidence:

- Extensive operational/docs coverage (Quickstart, FAQ, Troubleshooting, runbooks, release notes).
- v2.3 architecture and milestone documents are cross-linked from README.

Gap to secure HD+:

- Keep release/process docs fully synchronized with latest tag/run evidence and grading artifacts.

## C. Verified CI Evidence

- Supply-chain success on `master` after stabilization:
  - https://github.com/JasonEran/Aether-Guard/actions/runs/22424248311
- Release published:
  - https://github.com/JasonEran/Aether-Guard/releases/tag/v2.3.0

## D. HD+ Closure Backlog (Priority Order)

1. Add requirements estimation/prioritization/budget page.
2. Add external-tool design artifacts (UML/ERD/UI prototype evidence).
3. Add AI/Web automated tests (beyond syntax/lint/build).
4. Add agile iteration log (demo + feedback + retrospective).
5. Final rubric checklist with direct links for assessor one-click verification.
