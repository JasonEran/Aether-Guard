# Aether-Guard v2.3 Architecture: Multimodal Predictive Cloud Operating System

This document defines the v2.3 roadmap after the v2.2 baseline. It is a forward-looking architecture plan that
evolves Aether-Guard from reactive thresholding to predictive, multimodal risk allocation for spot workloads.

## 1) Executive Summary and Scope

- **Problem**: Spot markets are volatile. Reactive heuristics trigger either too late (risking outages) or too early
  (eroding savings).
- **Goal**: Predict event-driven volatility by fusing internal telemetry with external cloud signals, enabling
  dynamic risk allocation.
- **Scope**: Architectural and algorithmic direction. It does not change the v2.2 implementation contract.

## 2) Strategic Context

- **Current state**: C++17 agent + .NET control plane + Python AI engine + Next.js dashboard, secured with SPIFFE/SPIRE
  and API keys.
- **Limitation**: Heuristic AI reacts to immediate signals. It lacks a model of external context (provider incidents,
  regional capacity pressure, maintenance events).

## 3) Vision: Predictive Cloud Operating System

We target an optimal risk allocation between service stability (SLO adherence) and cost efficiency (spot savings)
under stochastic market conditions. Achieving this requires a **Market Dynamics Model** that correlates internal
telemetry with external signals, without attempting to simulate the full market.

Four pivots define the transition:

- **Algorithmic pivot**: from static regression to time-series forecasting (TSMixer) augmented by NLP.
- **Architectural pivot**: from centralized control to **federated inference** with embedded AI on agents.
- **Informational pivot**: from unimodal telemetry to **multimodal intelligence** (numbers plus semantics).
- **Methodological pivot**: from synthetic simulation to **historical trace replay and backtesting**.

## 4) Multimodal Prediction Architecture

### 4.0 System Overview (Data Flow)

```mermaid
flowchart LR
  Agent[Agent (C++ TSMixer)] -->|Telemetry| Core[Control Plane (.NET)]
  Core -->|Commands| Agent
  Signals[External Signals] -->|Status/Incidents| NLP[Semantic Enrichment (AI Engine)]
  NLP -->|S_v, P_v, B_s| Core
  Core -->|Semantic Features (gRPC Heartbeat)| Agent
  Core -->|Telemetry + Signals| DB[(PostgreSQL/Timescale)]
  Core --> Dashboard[Dashboard (Next.js)]
```

### 4.1 Numerical Stream (TSMixer)

TSMixer is an MLP-only model that explicitly mixes information across time and feature dimensions. It is lightweight
enough for low-latency inference on the agent.

- **Time-mixing MLP**: learns temporal patterns (for example, CPU spike persistence).
- **Feature-mixing MLP**: learns cross-signal correlations (for example, memory dirty rate plus disk IOPS).
- **Why TSMixer (vs Transformers)**: lower complexity, stable training on multivariate series, and small model
  footprint enable sub-millisecond inference on agents without attention overhead.

### 4.2 Semantic Stream (Domain NLP)

Telemetry alone misses off-chart events. We introduce a semantic pipeline for cloud operations signals:

- **Input sources**: provider health dashboards, incident reports, capacity advisories, maintenance notices, regional
  outages, and major upstream dependency alerts.
- **Model**: a domain-adapted transformer (BERT-class). If economic signals are used, FinBERT is a reasonable baseline
  for finance-domain text; an LLM summarizer handles longer advisories and provider policy updates.
- **Outputs (standardized)**:
  - `schemaVersion`: semantic vector schema version.
  - `S_v`: sentiment vector (normalized polarity + severity).
  - `P_v`: volatility probability (0-1).
  - `B_s`: supply or capacity bias (long-horizon).

Optional signal extensions (if governance allows):

- Provider pricing methodology updates, capacity planning statements, or public filings.
- External feeds (for example, NewsAPI/Alpha Vantage) for incident-related headlines.

### 4.3 Fusion Layer (Correlated Prediction)

Semantic signals are treated as **exogenous variables** in the forecasting head. The objective becomes
`P(Preemption | Telemetry, ExternalSignals)` rather than purely `P(Preemption | Telemetry)`.

Formally:

```
X_t = [x_{t-k}, ..., x_t]          # numerical window
Z_t = [S_v, P_v, B_s]              # semantic features
h_t = TSMixer(X_t, Z_t)            # fused representation
P_preempt = sigma(W * h_t)         # preemption probability
```

Example causal pattern:

> GPU shortage advisory + rising spot volatility -> preemption probability spikes within 60 minutes.

## 5) Federated Inference

- **Agent**: runs low-latency TSMixer locally for numerical predictions.
- **Control plane**: computes semantic vectors asynchronously and pushes them to agents via gRPC heartbeat.
- **Result**: a lightweight agent with high-quality semantic context, without embedding large NLP models on edge nodes.

## 6) Dynamic Risk Management

Static thresholds are replaced by dynamic risk allocation:

```
U = Savings - (RiskFactor * CostOfFailure) - MigrationCost
```

We also define a simplified decision score for explainability:

```
DecisionScore = lambda * SavingsScore + (1 - lambda) * ConfidenceScore
```

- **Confidence Score (C)**: correlation strength between telemetry and external signals.
- **Risk Allocation Factor (alpha)**: scales sensitivity to preemption signals.
  - **Risk-on**: stable telemetry + neutral signals -> lower alpha -> maximize savings.
  - **Risk-off**: negative signals + rising volatility -> higher alpha -> preemptive migration.

Reference pseudocode:

```
if P_preempt * alpha > threshold:
    migrate()
else:
    stay_on_spot()
```

## 7) Training Methodology (Historical Replay, Not Simulation)

- Reject agent-based market simulation as speculative and brittle.
- Adopt historical trace replay and backtesting:
  - Merge cluster traces with spot price history and incident/news archives.
  - Replay the agent through real timelines to learn causal patterns.
  - Validate on held-out stress windows (for example, major regional outages or GPU shortages).

Recommended public sources for replay:

- AWS Spot Price History API (pricing time series).
- Alibaba or Google cluster trace datasets (resource usage).
- Public incident and status archives for semantic signals.

## 8) Implementation Implications (Preview)

- **Data ingestion**: add connectors for provider status feeds and incident streams.
- **Schema**: store semantic vectors and link them to telemetry windows.
- **Control plane**: enrich agent heartbeat payloads with semantic features.
- **AI engine**: host inference services for semantic extraction and fusion.

## 9) Literature Anchors (Design Justification)

- FinBERT for finance-domain sentiment analysis (optional baseline for economic signals).
- TSMixer for efficient multivariate time-series forecasting.
- Dynamic risk management literature for adaptive thresholds in volatile systems.

## 10) Non-goals

- Full market simulation of other tenants.
- Replacing existing v2.2 APIs without a compatibility layer.
