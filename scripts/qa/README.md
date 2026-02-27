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

## `verify_tdd_evidence.py`

Verify that the TDD ledger is consistent with git history:

- commit SHAs exist
- the listed commits touch the listed test/implementation files
- evidence level rule is satisfied:
  - `A`: test commit date < implementation commit date
  - `B`: test commit SHA == implementation commit SHA
  - `C`: test commit date > implementation anchor commit date

### Usage

```bash
python scripts/qa/verify_tdd_evidence.py \
  --ledger docs/CP3407-TDD-Ledger-v2.3.json \
  --output .tmp/tdd-ledger-report.md
```

Exit codes:

- `0` => ledger verification passed
- `1` => ledger verification failed
