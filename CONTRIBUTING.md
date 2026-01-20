# Contributing to Aether-Guard

Thank you for your interest in contributing to Aether-Guard. This project is a
public good effort to provide free infrastructure monitoring for enterprises.
We welcome contributions across the full stack: C++, C#, Python, and
TypeScript/Next.js.

## Code of Conduct

By participating, you are expected to uphold the Code of Conduct.
Please read CODE_OF_CONDUCT.md before contributing.

## Project Structure

- src/services/agent-cpp - C++ agent
- src/services/core-dotnet - ASP.NET Core API
- src/services/ai-engine - FastAPI AI service
- src/web/dashboard - Next.js dashboard
- docker-compose.yml - local orchestration

## Development Prerequisites

- Docker and Docker Compose
- Node.js 20+ and npm
- .NET SDK 8.0
- Python 3.10+
- CMake 3.10+ and a C++17 compiler

## Local Setup

### Docker (recommended)

```bash
docker compose up --build -d
```

### Dashboard (Next.js)

```bash
cd src/web/dashboard
npm install
npm run dev
```

### Core API (.NET)

```bash
cd src/services/core-dotnet/AetherGuard.Core
dotnet restore
dotnet run
```

### AI Engine (Python)

```bash
cd src/services/ai-engine
python -m venv .venv
.venv/Scripts/activate
pip install -r requirements.txt
uvicorn main:app --host 0.0.0.0 --port 8000
```

### C++ Agent

```bash
cd src/services/agent-cpp
cmake -S . -B build
cmake --build build
./build/AetherAgent
```

## Testing and Quality

- C# tests: add and run with `dotnet test` when tests exist.
- Dashboard linting: `npm run lint`.
- Python: keep code compatible with Python 3.10+.
- C++: compile with C++17 and keep warnings minimal.

If you add new features, include tests when practical.

## Commit and PR Guidelines

- Keep changes focused and scoped to a single feature or fix.
- Use clear commit messages (imperative mood).
- Update README or documentation when behavior changes.
- For API changes, update relevant clients or document breaking changes.

### Pull Request Checklist

- [ ] Build succeeds locally or via Docker.
- [ ] Tests and lint pass (if applicable).
- [ ] Documentation updated (if applicable).
- [ ] No secrets or credentials committed.

## Issue Reporting

Please use the issue templates for bug reports and feature requests.
Include environment details and reproduction steps.

## Security

Security issues should not be reported via public issues. Please follow
SECURITY.md for the reporting process.

## License

By contributing, you agree that your contributions will be licensed under
the MIT License.
