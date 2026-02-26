# v2.3 Class Diagrams (Mermaid)

This document provides UML-style class diagrams for the four major service domains:

- Core (.NET)
- Agent (C++)
- AI Engine (Python/FastAPI)
- Web Dashboard (Next.js)

## Cross-Service Overview

```mermaid
classDiagram
  class AgentService {
    +register()
    +heartbeat()
    +checkpoint()
    +restore()
  }

  class CoreApi {
    +ingestTelemetry()
    +queueCommand()
    +runMigrationCycle()
    +getDashboardLatest()
  }

  class AiEngine {
    +analyze()
    +enrichSignals()
    +enrichSignalsBatch()
  }

  class WebDashboard {
    +fetchFleetStatus()
    +fetchRiskHistory()
    +renderExplainability()
  }

  class PostgreSQL
  class RabbitMQ
  class SnapshotStorage

  AgentService --> CoreApi : telemetry/heartbeat
  CoreApi --> AiEngine : risk + enrichment calls
  CoreApi --> PostgreSQL : persistence
  CoreApi --> RabbitMQ : async ingestion
  CoreApi --> SnapshotStorage : snapshot artifacts
  WebDashboard --> CoreApi : dashboard APIs
```

## Core (.NET)

```mermaid
classDiagram
  class DashboardController {
    +GetLatest()
    +GetHistory()
  }

  class ControlPlaneService {
    +QueueCommandAsync()
    +GetLatestAsync()
    +GetHistoryAsync()
  }

  class TelemetryIngestionService {
    +IngestAsync()
  }

  class MigrationOrchestrator {
    +RunMigrationCycle()
  }

  class DynamicRiskPolicy {
    +Evaluate()
    +ComputeAlpha()
  }

  class AnalysisService {
    +AnalyzeAsync()
  }

  class CommandService {
    +QueueCommand()
  }

  class TelemetryStore {
    +Update()
    +GetLatest()
  }

  class ApplicationDbContext
  class TelemetryPayload
  class AnalysisResult

  DashboardController --> ControlPlaneService
  ControlPlaneService --> TelemetryStore
  ControlPlaneService --> CommandService
  ControlPlaneService --> DynamicRiskPolicy
  ControlPlaneService --> ApplicationDbContext
  TelemetryIngestionService --> AnalysisService
  TelemetryIngestionService --> TelemetryStore
  MigrationOrchestrator --> DynamicRiskPolicy
  MigrationOrchestrator --> AnalysisService
  MigrationOrchestrator --> CommandService
  TelemetryStore --> TelemetryPayload
  TelemetryStore --> AnalysisResult
```

## Agent (C++)

```mermaid
classDiagram
  class NetworkClient {
    +Register()
    +Heartbeat()
    +PollCommands()
    +SendFeedback()
  }

  class CommandPoller {
    +Poll()
  }

  class CommandDispatcher {
    +Dispatch()
  }

  class InferenceEngine {
    +Initialize()
    +Evaluate()
  }

  class ArchiveManager {
    +CreateSnapshotArchive()
    +RestoreFromSnapshot()
  }

  class CriuManager {
    +Checkpoint()
    +Restore()
  }

  class LifecycleManager {
    +Start()
    +Stop()
  }

  class SemanticFeatures

  LifecycleManager --> NetworkClient
  LifecycleManager --> CommandPoller
  CommandPoller --> CommandDispatcher
  CommandDispatcher --> ArchiveManager
  CommandDispatcher --> InferenceEngine
  ArchiveManager --> CriuManager
  InferenceEngine --> SemanticFeatures
```

## AI Engine (Python / FastAPI)

```mermaid
classDiagram
  class FastAPIApp {
    +POST /analyze
    +POST /signals/enrich
    +POST /signals/enrich/batch
  }

  class RiskModel {
    +analyzeTelemetry()
  }

  class SignalEnrichmentModel {
    +enrichSignal()
    +enrichBatch()
  }

  class FinBertAdapter {
    +predictSentiment()
  }

  class Summarizer {
    +summarize()
  }

  FastAPIApp --> RiskModel
  FastAPIApp --> SignalEnrichmentModel
  SignalEnrichmentModel --> FinBertAdapter
  SignalEnrichmentModel --> Summarizer
```

## Web Dashboard (Next.js)

```mermaid
classDiagram
  class DashboardClient {
    +load()
    +handleSimulateChaos()
  }

  class ApiLib {
    +fetchFleetStatus()
    +fetchRiskHistory()
    +fetchAuditLogs()
  }

  class ExplainabilityPanel {
    +render(alpha, P_preempt, topSignals)
  }

  class ExternalSignalsPanel
  class ControlPanel
  class HistoryChart

  DashboardClient --> ApiLib
  DashboardClient --> ExplainabilityPanel
  DashboardClient --> ExternalSignalsPanel
  DashboardClient --> ControlPanel
  DashboardClient --> HistoryChart
```
