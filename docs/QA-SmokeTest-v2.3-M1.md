# v2.3 Milestone 1 Smoke Test Checklist (Semantic Enrichment)

This checklist validates the v2.3 Milestone 1 flow: FinBERT/heuristic enrichment,
summary generation, schema versioning, and batch enrichment integration.

## Preconditions

- Docker Desktop running
- `COMMAND_API_KEY` set
- External signals enabled

## Start stack

```bash
# PowerShell
$env:COMMAND_API_KEY="changeme"
$env:ExternalSignals__Enabled="true"

docker compose up --build -d
```

## Verify enrichment schema metadata

```bash
curl http://localhost:8000/signals/enrich/schema
```

Expected:
- `schemaVersion` is present
- `batchEndpoint` is `/signals/enrich/batch`
- `fields` contains `S_v`, `P_v`, `B_s`

## Verify single enrichment endpoint

```bash
curl -X POST http://localhost:8000/signals/enrich \
  -H "Content-Type: application/json" \
  -d '{"documents":[{"source":"aws","title":"Service disruption in us-east-1","summary":"Investigating elevated errors."}]}'
```

Expected:
- HTTP 200
- Response includes `schemaVersion`, `S_v`, `P_v`, `B_s`

## Verify batch enrichment endpoint

```bash
curl -X POST http://localhost:8000/signals/enrich/batch \
  -H "Content-Type: application/json" \
  -d '{"documents":[{"source":"aws","title":"Service disruption in us-east-1","summary":"Investigating elevated errors."},{"source":"gcp","title":"RESOLVED: incident in us-central1","summary":"Service recovered."}]}'
```

Expected:
- HTTP 200
- `vectors` length matches input document count
- Each vector includes `index`, `S_v`, `P_v`, `B_s`

## Verify summarization endpoint

```bash
curl -X POST http://localhost:8000/signals/summarize \
  -H "Content-Type: application/json" \
  -d '{"documents":[{"source":"aws","title":"AWS incident update","summary":"Service degradation observed in us-east-1 with intermittent errors and elevated latency."}],"maxChars":160}'
```

Expected:
- HTTP 200
- Response includes `schemaVersion` and non-empty `summaries`

## Verify Core integration result

```bash
curl http://localhost:5000/api/v1/signals?limit=10
```

Expected (after at least one ingest cycle):
- At least one signal has `summarySchemaVersion` or `enrichmentSchemaVersion`
- Enriched vectors are persisted (`sentimentNegative`, `sentimentNeutral`, `sentimentPositive`, `volatilityProbability`, `supplyBias`)

## Optional throughput sanity check

Send 100+ documents to `/signals/enrich/batch` and confirm request latency remains within your
target budget for deployment (for example, under a few seconds in local Docker with heuristic mode).

## Stop stack

```bash
docker compose down
```
