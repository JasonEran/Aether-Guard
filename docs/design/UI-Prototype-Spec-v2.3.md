# UI Prototype Spec (v2.3)

This spec is prepared for creating rubric-facing prototype evidence in a UI prototyping tool
(NinjaMock/Figma/Adobe XD). It maps directly to implemented dashboard components.

## Screen Inventory

1. Login screen
2. Dashboard overview
3. External signals drill-down
4. Explainability panel
5. Control panel (fire drill + command operations)
6. History chart / trend view

## Screen Requirements

### 1) Login Screen

- Fields: username, password.
- CTA: Sign in.
- Error states: invalid credential, service unavailable.
- Mapping: NextAuth credentials flow.

### 2) Dashboard Overview

- KPI cards: fleet status, risk trend, active alerts.
- Table/list: latest agents and latest telemetry snapshot.
- Global nav to Explainability / Signals / Control sections.

### 3) External Signals Drill-Down

- Feed health cards (AWS/GCP/Azure status and freshness).
- Signal list with source, severity, region, published timestamp.
- Filter controls: source, severity, time range.
- Mapping component: `ExternalSignalsPanel.tsx`.

### 4) Explainability Panel

- Display fields: `alpha`, `P_preempt`, decision score/confidence.
- Top fused signals list with rationale snippets.
- State badges: risk-on / risk-off decision.
- Mapping component: `ExplainabilityPanel.tsx`.

### 5) Control Panel

- Actions: trigger simulation/fire drill, queue migration commands.
- Confirmation modal for sensitive operations.
- Audit/feedback panel for command results.
- Mapping component: `ControlPanel.tsx`.

### 6) History Chart / Trend

- Time-series chart for telemetry/risk over selected range.
- Toggle between agent-level and fleet-level views.
- Mapping component: `HistoryChart.tsx`.

## Prototype Acceptance Checklist

- [ ] All six screens exported (PNG/PDF) from external prototype tool.
- [ ] Navigation links between screens are wired for click-through demo.
- [ ] At least one happy-path and one failure-path interaction is captured.
- [ ] Artifact share link is public/read-only and added to `docs/CP3407-Design-Artifacts.md`.

## Component Mapping (Implemented Code)

- `src/web/dashboard/components/ExternalSignalsPanel.tsx`
- `src/web/dashboard/components/ExplainabilityPanel.tsx`
- `src/web/dashboard/components/ControlPanel.tsx`
- `src/web/dashboard/components/HistoryChart.tsx`
- `src/web/dashboard/app/DashboardClient.tsx`
