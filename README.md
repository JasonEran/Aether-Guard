![Aether-Guard Logo](./aether-guard.png)

# Aether-Guard

[![C++](https://img.shields.io/badge/C%2B%2B-17-00599C?logo=c%2B%2B&logoColor=white)](https://isocpp.org/)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![FastAPI](https://img.shields.io/badge/FastAPI-0.100+-009688?logo=fastapi&logoColor=white)](https://fastapi.tiangolo.com/)
[![Next.js](https://img.shields.io/badge/Next.js-16-000000?logo=next.js&logoColor=white)](https://nextjs.org/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-15-336791?logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![Docker](https://img.shields.io/badge/Docker-Compose-2496ED?logo=docker&logoColor=white)](https://www.docker.com/)

Aether-Guard is a distributed infrastructure monitoring system designed as a
public good project for free enterprise monitoring. It combines a high-
performance C++ agent with a .NET Core API, a Python AI engine, and a Next.js
dashboard to deliver real-time telemetry, lightweight anomaly analysis, and
historical insights.

## Project Status

- Stage: MVP completed, preparing for public release
- License: MIT
- Authors: Qi Junyi, Xiao Erdong (2026)

## Architecture

Data flow:

Agent (C++) -> Core API (.NET) -> AI Engine (FastAPI) -> Core API -> PostgreSQL -> Dashboard (Next.js)

Control plane vs data plane:

- Data plane: the C++ agent collects system metrics and posts telemetry.
- Control plane: the Core API validates, enriches with AI analysis, persists
  data, and exposes read APIs for the dashboard.

## Services

- agent-service: C++ telemetry agent reading /proc on Linux and posting to Core.
- core-service: ASP.NET Core API for ingestion, analysis, and data access.
- ai-service: FastAPI service for anomaly classification (placeholder model).
- web-service: Next.js dashboard with authentication and visualization.
- db: PostgreSQL for persistence.

## Ports

- Core API: http://localhost:5000
- Dashboard: http://localhost:3000
- AI Engine: http://localhost:8000
- PostgreSQL: localhost:5432

## Quick Start (Docker)

```bash
docker compose up --build -d
```

Open the dashboard at http://localhost:3000.

### Default Login (Development)

- Username: admin
- Password: admin123

Override via environment variables:

- DASHBOARD_ADMIN_USER
- DASHBOARD_ADMIN_PASSWORD

## Configuration

Core API database connection (docker-compose.yml):

- ConnectionStrings__DefaultConnection=Host=db;Database=AetherGuardDb;Username=postgres;Password=password

Dashboard auth (docker-compose.yml):

- AUTH_SECRET=super-secret-key
- AUTH_TRUST_HOST=true

For production, set a strong AUTH_SECRET and use a secret manager.

## API Overview

Core API:

- POST /api/v1/ingestion - receive telemetry from agent
- GET /api/v1/dashboard/latest - latest telemetry + AI analysis
- GET /api/v1/dashboard/history - last 20 telemetry records (chronological)

AI Engine:

- POST /analyze - classify telemetry as Normal or Critical

## Data Model

TelemetryRecord persisted to PostgreSQL:

- AgentId
- CpuUsage
- MemoryUsage
- AiStatus
- AiConfidence
- Timestamp (UTC)

The Core API currently uses EnsureCreated() on startup for schema creation.

## Development

Dashboard:

```bash
cd src/web/dashboard
npm install
npm run dev
```

Core API:

```bash
cd src/services/core-dotnet/AetherGuard.Core
dotnet restore
dotnet run
```

AI Engine:

```bash
cd src/services/ai-engine
python -m venv .venv
.venv/Scripts/activate
pip install -r requirements.txt
uvicorn main:app --host 0.0.0.0 --port 8000
```

C++ Agent:

```bash
cd src/services/agent-cpp
cmake -S . -B build
cmake --build build
./build/AetherAgent
```

## Security Notes

- Authentication uses NextAuth Credentials for the MVP. Replace with an
  external identity provider for production.
- CORS is limited to http://localhost:3000 in development.
- Secrets and credentials must be rotated for any public deployment.

## Roadmap (Industrialization)

Phase 1: Operational foundation
- Agent registration and heartbeat
- Offline detection and node state in the dashboard
- Policy-based authorization in Core (RBAC)
- Alert debouncing and suppression windows

Phase 2: Actionability and control
- Command-and-control loop (server-issued commands, agent execution)
- Runbook links and recommended actions
- ChatOps integration (Slack/Webhook actions)

Phase 3: Insight and scalability
- Time-series optimized storage (TimescaleDB or InfluxDB)
- Root-cause analysis payloads from AI
- Correlated event overlays (deployments and incidents)

Phase 4: Productization
- Role-based dashboards for SRE, Admin, and Finance
- SLA and cost reporting
- Audit trails, approval workflows, and zero-trust access controls
- Mobile-friendly workflows and multi-channel alerting

## Contributing

Please read CONTRIBUTING.md for setup, workflow, and PR guidelines.

## License

MIT License. See LICENSE.
