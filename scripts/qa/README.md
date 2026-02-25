# v2.3 M3 Canary QA Scripts

## `evaluate_m3_canary.py`

Evaluate canary window metrics and produce a deterministic decision:

- `promote`
- `hold`
- `rollback`

### Usage

```bash
python scripts/qa/evaluate_m3_canary.py \
  --input .tmp/canary-input.json \
  --output .tmp/canary-decision.json \
  --summary-md .tmp/canary-decision.md
```

Exit codes:

- `0` => promote
- `10` => hold
- `20` => rollback
