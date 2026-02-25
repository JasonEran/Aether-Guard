# v2.3 M4 Dynamic Risk Allocation (Core)

This document captures issue #43.

## Alpha Computation

Core computes dynamic `alpha` via `DynamicRiskPolicy`:

```
sentiment_pressure = max(0, S_neg - S_pos)
alpha = clamp(
  base_alpha
  + volatility_weight * P_v
  + sentiment_weight * sentiment_pressure,
  min_alpha,
  max_alpha
)
```

When `rebalanceSignal=true`, `alpha` is forced to `max_alpha`.

## Decision Score

```
decision_score = clamp(P_preempt * alpha, 0, 1)
migrate if decision_score >= decision_threshold
```

`P_preempt` is derived from AI analysis confidence/prediction with fallback handling for `Unavailable`.

## Guardrails

Implemented in migration orchestration:

- **Cooldown guardrail**: block migration if source agent migrated within `CooldownMinutes`.
- **Max-rate guardrail**: block migration if completed migrations in the last hour exceed `MaxMigrationsPerHour`.

Guardrail blocks take precedence over score-based migration.

## Config Section

`DynamicRisk`:

- `BaseAlpha`
- `VolatilityWeight`
- `SentimentWeight`
- `MinAlpha`
- `MaxAlpha`
- `DecisionThreshold`
- `CooldownMinutes`
- `MaxMigrationsPerHour`

## Tests

`DynamicRiskPolicyTests` cover edge cases:

- alpha upper clamp
- alpha lower clamp
- cooldown guardrail block
- max-rate guardrail block
- threshold crossing migration decision
