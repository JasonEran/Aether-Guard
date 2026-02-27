# CP3407 Toolchain and Build Rationale (v2.3)

Last updated: 2026-02-27

This page explains what development/build tools were used and why, for CP3407
criterion 6 ("exemplary use of software development tools, building tools and external libraries").

## 1) Core Stack Choices

| Area | Tooling | Why this choice |
| --- | --- | --- |
| Backend control plane | .NET 8 + ASP.NET Core + gRPC/JSON transcoding | Strong typed contracts, mature web stack, and dual protocol support |
| Agent runtime | C++17 + CMake + ctest | Low-level host interaction, performance, and portable native builds |
| AI service | Python 3.11 + FastAPI | Fast iteration for model-serving and data-processing workflows |
| Web UI | Next.js 16 + TypeScript + ESLint | Modern SSR/SPA hybrid UI with strong type/lint tooling |
| Database | PostgreSQL/TimescaleDB | Reliable relational core with time-series friendly capability |
| Messaging/cache | RabbitMQ + Redis | Asynchronous ingestion plus dedup/backpressure support |
| Object storage | MinIO/S3 API | Snapshot artifact retention and cloud-compatible storage contract |

## 2) Engineering Workflow Tooling

| Concern | Tooling | What it enables |
| --- | --- | --- |
| Source control | Git + GitHub Issues/PR templates | Traceable planning, review, and release history |
| CI quality gate | GitHub Actions (`quality-gate.yml`) | Automated lint/build/test enforcement across all stacks |
| Supply chain | SBOM + cosign + SLSA workflows | Verifiable artifacts and provenance evidence |
| Observability | OpenTelemetry + Jaeger | Trace-based debugging and milestone smoke evidence |
| Local deployment | Docker Compose | One-command multi-service bring-up for demo and testing |

## 3) External Design Tools (Rubric Design Requirement)

- UML/architecture rendering: PlantUML
- ERD rendering: PlantUML ERD + DBML source
- UI prototype/wireframes: PlantUML Salt

Registry and share links:

- `docs/CP3407-Design-Artifacts.md`

## 4) Build and Reproducibility Standard

- Build/test commands are documented in `README.md` and `docs/Quickstart.md`.
- CI artifacts and workflow history provide reproducible evidence for assessor replay.
- Milestone evidence links are centralized in `docs/CP3407-Assessor-OneClick.md`.
