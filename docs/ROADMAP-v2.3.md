# Aether-Guard v2.3 Roadmap (Best-Practice Delivery Plan)

This roadmap translates the v2.3 architecture into a staged, testable delivery plan. Each milestone is scoped to
minimize integration risk while preserving backward compatibility with the v2.2 baseline.

## Guiding Principles

- Keep v2.2 stable: no breaking API changes without compatibility shims.
- Ship in small, reversible steps with clear validation criteria.
- Prefer real-world data replay and backtesting over synthetic simulation.

## Milestones

### Milestone 0: Data Foundation (Signals Ingestion)

**Goal**: Introduce external cloud signals and store them alongside telemetry windows.
**Status**: Completed (2026-02-11)

- Add connectors for provider status feeds and incident streams.
- Normalize signal schema (timestamp, region, severity, source, summary).
- Store in the control-plane database with retention policies.

**Exit Criteria**

- Signals are persisted and queryable by time window.
- No impact on v2.2 telemetry ingestion performance.

### Milestone 1: Semantic Enrichment Service

**Goal**: Extract semantic vectors from signals without impacting core latency.
**Status**: Completed (2026-02-19)

- Add NLP service for incident sentiment and volatility likelihood (FinBERT or domain BERT).
- Add LLM summarizer for longer advisories and policy updates.
- Define and version `S_v`, `P_v`, and `B_s` output schemas.
- Expose a batch API for enrichment and caching.

**Exit Criteria**

- Enrichment throughput supports expected signal volume.
- Outputs are versioned and validated.
- FinBERT/semantic enrichment is operational (with heuristic fallback).

### Milestone 2: Fusion and Forecasting (Offline)

**Goal**: Train and evaluate models with historical replay.

- Add TSMixer baseline for numerical telemetry (PyTorch), with export to ONNX for agent inference.
- Fuse exogenous semantic vectors for `P(Preemption | Telemetry, Signals)`.
- Backtest on historical windows and held-out stress periods.

**Exit Criteria**

- Backtesting shows measurable improvement vs heuristic baseline.
- Model artifacts are reproducible and versioned.

### Milestone 3: Federated Inference (Online)

**Goal**: Deliver semantic vectors to agents and run local inference.

- Extend gRPC heartbeat payload with semantic features.
- Deploy lightweight on-agent inference (TSMixer).
- Add per-agent feature gating and fallback to v2.2 heuristics.

**Exit Criteria**

- Agent inference stays within latency budgets.
- Safe rollback to v2.2 heuristics is verified.

### Milestone 4: Dynamic Risk Management

**Goal**: Replace static thresholds with dynamic risk allocation.

- Implement confidence score and risk allocation factor.
- Add guardrails (max migration rate, minimum cool-down windows).
- Surface decision explainability in dashboard.

**Exit Criteria**

- SLO impact is neutral or improved.
- Savings increase without higher incident rate.

## Validation Strategy

- Historical trace replay and backtesting.
- Canary deployment with automatic rollback.
- End-to-end regression tests on v2.2 compatibility.

## Data Sources and Tooling (Recommended)

- **Spot price history**: AWS Spot Price History API.
- **Telemetry traces**: Alibaba or Google cluster traces.
- **Incident signals**: provider health dashboards, incident/status RSS feeds.
- **Optional economic signals**: vendor advisories, capacity planning updates, curated news feeds.
- **NLP tooling**: Hugging Face transformers for FinBERT/domain BERT; LLM summarization via the AI engine.

## References (Design Justification)

- FinBERT (financial sentiment baseline).
- TSMixer (all-MLP time-series forecasting).
- Dynamic risk management literature for adaptive thresholds.

## Non-goals (v2.3)

- Full market simulation of all cloud tenants.
- Removal of existing v2.2 API surfaces.
