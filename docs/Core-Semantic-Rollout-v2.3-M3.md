# v2.3 M3 Core Push + Per-Agent Gating

This document captures issue #41.

## Scope

- Control-plane rollout gate for agent local inference.
- Safe fallback behavior preserved for v2.2 compatibility.

## Changes

- Protobuf (`agent_service.proto`)
  - `AgentConfig.enable_local_inference = 6` (additive field).
- Core rollout options:
  - `AgentInference:EnableLocalInferenceRollout`
  - `AgentInference:RolloutPercentage`
- Register response config now carries `enableLocalInference`.
- Heartbeat semantic payload remains active and includes fallback vectors when enrichment is unavailable.

## Rollout Logic

`AgentWorkflowService` computes a deterministic per-agent rollout bucket from stable agent key (hostname):

- Global rollout disabled => `enable_local_inference=false`
- Rollout 0 => disabled for all agents
- Rollout 100 => enabled for all agents
- Rollout N (1-99) => deterministic subset enabled

## Compatibility

- New proto field is additive; existing agents remain wire-compatible.
- Agent runtime still honors rollback (`AG_M3_FORCE_V22_FALLBACK`) and fail-open behavior.

## Validation

- New tests in `AetherGuard.Core.Tests`:
  - rollout disabled -> local inference off
  - rollout 100% -> local inference on
