# v2.3 M3 Canary + Rollback Plan

This document captures issue #42.

## Goal

Ship online inference safely with explicit rollout gates and automatic rollback triggers.

## Canary Plan

1. **Stage 0 (shadow)**  
   Enable heartbeat semantic payload and local inference logging only.
2. **Stage 1 (1-5%)**  
   Set `AgentInference:EnableLocalInferenceRollout=true`, `RolloutPercentage=5`.
3. **Stage 2 (10-25%)**  
   Increase rollout only if no rollback trigger is hit for two full canary windows.
4. **Stage 3 (50%+)**  
   Expand progressively with the same guardrails.
5. **Stage 4 (100%)**  
   Promote after stable windows and no critical incidents.

## Automated Rollback Triggers

Use `scripts/qa/evaluate_m3_canary.py` with canary metrics input.

Rollback is required when any critical threshold is breached:

- `critical_incident_count > 0`
- `heartbeat_failure_rate > 0.05`
- `inference_error_rate > 0.02`
- `p95_inference_latency_ms > 50`
- `false_positive_rate_delta > 0.10`
- `preempt_decision_rate_delta > 0.15`

## Automation Output

Script output:

- JSON decision artifact (`promote` / `hold` / `rollback`)
- optional markdown summary
- exit code suitable for CI gate:
  - `0` promote
  - `10` hold
  - `20` rollback

## Rollback Procedure (Immediate)

1. Set `AG_M3_FORCE_V22_FALLBACK=true` on canary agents.
2. Disable local inference rollout (`AgentInference:RolloutPercentage=0`).
3. Keep semantic heartbeat transport enabled for observability.
4. Open incident ticket and attach decision artifacts.

## Evidence Artifacts

- Script: `scripts/qa/evaluate_m3_canary.py`
- Script usage: `scripts/qa/README.md`
