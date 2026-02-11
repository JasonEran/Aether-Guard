import logging
import os
from contextlib import asynccontextmanager
from dataclasses import dataclass
from functools import lru_cache
from typing import Iterable

from fastapi import FastAPI
from pydantic import BaseModel, Field, ConfigDict
from opentelemetry import trace
from opentelemetry.exporter.otlp.proto.http.trace_exporter import OTLPSpanExporter
from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor
from opentelemetry.instrumentation.requests import RequestsInstrumentor
from opentelemetry.sdk.resources import Resource
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor

from model import RiskScorer

logger = logging.getLogger("uvicorn.error")
scorer = RiskScorer()
ENRICHMENT_SCHEMA_VERSION = "1.0"

DEFAULT_FINBERT_MODEL = os.getenv("AI_FINBERT_MODEL", "ProsusAI/finbert")
ENRICHMENT_PROVIDER = os.getenv("AI_ENRICH_PROVIDER", "finbert").lower()
ENRICHMENT_MAX_CHARS = int(os.getenv("AI_ENRICH_MAX_CHARS", "2000"))
ENRICHMENT_CACHE_SIZE = int(os.getenv("AI_ENRICH_CACHE_SIZE", "1024"))


@asynccontextmanager
async def lifespan(app_instance: FastAPI):
    configure_tracing()
    app_instance.state.scorer = scorer
    app_instance.state.enricher = build_enricher()
    logger.info("AI Engine Online.")
    yield


app = FastAPI(lifespan=lifespan)


class RiskPayload(BaseModel):
    spot_price_history: list[float] = Field(default_factory=list, alias="spotPriceHistory")
    rebalance_signal: bool = Field(alias="rebalanceSignal")
    capacity_score: float = Field(alias="capacityScore")

    model_config = ConfigDict(populate_by_name=True)


class SignalDocument(BaseModel):
    source: str
    title: str
    summary: str | None = None
    url: str | None = None
    region: str | None = None
    published_at: str | None = Field(default=None, alias="publishedAt")

    model_config = ConfigDict(populate_by_name=True)


class EnrichRequest(BaseModel):
    documents: list[SignalDocument]


class EnrichResponse(BaseModel):
    schema_version: str = Field(alias="schemaVersion", description="Semantic vector schema version.")
    s_v: list[float] = Field(
        alias="S_v",
        description="Sentiment vector [negative, neutral, positive], normalized to sum to 1.",
    )
    p_v: float = Field(
        alias="P_v",
        description="Volatility probability in the range [0, 1].",
    )
    b_s: float = Field(
        alias="B_s",
        description="Supply or capacity bias (long-horizon signal).",
    )

    model_config = ConfigDict(populate_by_name=True)


@app.get("/")
def root() -> dict:
    return {"status": "AI Engine Online"}


@app.post("/analyze")
def analyze(payload: RiskPayload) -> dict:
    scorer: RiskScorer = app.state.scorer
    assessment = scorer.assess_risk(
        payload.spot_price_history,
        payload.rebalance_signal,
        payload.capacity_score,
    )

    priority = assessment.Priority
    prediction = 100.0 if priority == "CRITICAL" else 0.0
    confidence = 0.95 if priority == "CRITICAL" else 0.8
    rca = "Rebalance signal asserted" if priority == "CRITICAL" else "Stable capacity"

    return {
        "status": priority,
        "prediction": prediction,
        "rca": rca,
        "confidence": confidence,
    }


@app.post("/signals/enrich", response_model=EnrichResponse)
def enrich_signals(payload: EnrichRequest) -> EnrichResponse:
    enricher: SemanticEnricher = app.state.enricher
    result = enricher.enrich(payload.documents)
    return EnrichResponse(
        schemaVersion=ENRICHMENT_SCHEMA_VERSION,
        S_v=result.s_v,
        P_v=result.p_v,
        B_s=result.b_s,
    )


@app.get("/signals/enrich/schema")
def enrich_schema() -> dict:
    return {
        "schemaVersion": ENRICHMENT_SCHEMA_VERSION,
        "fields": {
            "S_v": "Sentiment vector [negative, neutral, positive], normalized to sum to 1.",
            "P_v": "Volatility probability in [0, 1].",
            "B_s": "Supply or capacity bias (long-horizon signal).",
        },
    }


@dataclass
class EnrichResult:
    s_v: list[float]
    p_v: float
    b_s: float


class SemanticEnricher:
    def enrich(self, documents: Iterable[SignalDocument]) -> EnrichResult:  # pragma: no cover - interface
        raise NotImplementedError


class HeuristicEnricher(SemanticEnricher):
    negative_terms = ("outage", "disruption", "degraded", "incident", "latency", "unavailable")
    supply_terms = ("capacity", "shortage", "procurement", "inventory", "supply", "quota")

    def enrich(self, documents: Iterable[SignalDocument]) -> EnrichResult:
        combined = " ".join([self._doc_text(doc) for doc in documents]).lower()
        negative_hits = sum(term in combined for term in self.negative_terms)
        supply_hits = sum(term in combined for term in self.supply_terms)

        neg_score = 0.15 + 0.2 * negative_hits
        neutral_score = 0.6 - 0.1 * negative_hits
        pos_score = 1.0 - (neg_score + neutral_score)

        s_v = normalize_vector([neg_score, neutral_score, max(0.0, pos_score)])
        p_v = clamp(0.15 + 0.25 * negative_hits, 0.0, 1.0)
        b_s = clamp(0.05 * supply_hits, 0.0, 1.0)
        return EnrichResult(s_v=s_v, p_v=p_v, b_s=b_s)

    @staticmethod
    def _doc_text(doc: SignalDocument) -> str:
        summary = doc.summary or ""
        return f"{doc.title} {summary}".strip()


class FinbertEnricher(SemanticEnricher):
    def __init__(self, model_id: str, max_chars: int, cache_size: int) -> None:
        from transformers import pipeline
        import torch

        device = 0 if torch.cuda.is_available() else -1
        self._pipeline = pipeline(
            "sentiment-analysis",
            model=model_id,
            tokenizer=model_id,
            return_all_scores=True,
            device=device,
        )
        self._max_chars = max_chars
        self._cache = lru_cache(maxsize=cache_size)(self._score_text)

    def enrich(self, documents: Iterable[SignalDocument]) -> EnrichResult:
        scores = [self._cache(self._doc_text(doc)) for doc in documents]
        if not scores:
            return EnrichResult(s_v=[0.15, 0.7, 0.15], p_v=0.1, b_s=0.0)

        neg = sum(score[0] for score in scores) / len(scores)
        neutral = sum(score[1] for score in scores) / len(scores)
        pos = sum(score[2] for score in scores) / len(scores)
        s_v = normalize_vector([neg, neutral, pos])

        volatility_boost = max(0.0, neg - pos)
        p_v = clamp(0.1 + volatility_boost * 1.2, 0.0, 1.0)
        b_s = clamp(self._supply_bias(documents), 0.0, 1.0)
        return EnrichResult(s_v=s_v, p_v=p_v, b_s=b_s)

    def _score_text(self, text: str) -> list[float]:
        scores_raw = self._pipeline(text[: self._max_chars])
        scores = self._parse_scores(scores_raw)
        return normalize_vector(scores)

    @staticmethod
    def _parse_scores(raw) -> list[float]:
        if isinstance(raw, list) and raw and isinstance(raw[0], list):
            entries = raw[0]
        else:
            entries = raw

        label_map = {"negative": 0, "neutral": 1, "positive": 2}
        scores = [0.0, 0.0, 0.0]
        for entry in entries:
            label = entry.get("label", "").lower()
            score = float(entry.get("score", 0.0))
            if label in label_map:
                scores[label_map[label]] = score
        if sum(scores) == 0:
            return [0.15, 0.7, 0.15]
        return scores

    @staticmethod
    def _doc_text(doc: SignalDocument) -> str:
        summary = doc.summary or ""
        return f"{doc.title} {summary}".strip()

    @staticmethod
    def _supply_bias(documents: Iterable[SignalDocument]) -> float:
        supply_terms = ("capacity", "shortage", "procurement", "inventory", "supply", "quota")
        combined = " ".join([doc.title + " " + (doc.summary or "") for doc in documents]).lower()
        hits = sum(term in combined for term in supply_terms)
        return 0.05 * hits


def build_enricher() -> SemanticEnricher:
    if ENRICHMENT_PROVIDER == "finbert":
        try:
            logger.info("Loading FinBERT model %s for enrichment.", DEFAULT_FINBERT_MODEL)
            return FinbertEnricher(
                model_id=DEFAULT_FINBERT_MODEL,
                max_chars=ENRICHMENT_MAX_CHARS,
                cache_size=ENRICHMENT_CACHE_SIZE,
            )
        except Exception as exc:
            logger.warning("Failed to load FinBERT model, falling back to heuristics: %s", exc)
    return HeuristicEnricher()


def normalize_vector(values: list[float]) -> list[float]:
    total = sum(max(0.0, value) for value in values)
    if total <= 0:
        return [0.15, 0.7, 0.15]
    return [max(0.0, value) / total for value in values]


def clamp(value: float, min_value: float, max_value: float) -> float:
    return max(min_value, min(max_value, value))

def configure_tracing() -> None:
    endpoint = os.getenv("OTEL_EXPORTER_OTLP_ENDPOINT")
    if not endpoint:
        return

    service_name = os.getenv("OTEL_SERVICE_NAME", "aether-guard-ai")
    resource = Resource.create({"service.name": service_name})
    provider = TracerProvider(resource=resource)
    exporter = OTLPSpanExporter(endpoint=endpoint)
    provider.add_span_processor(BatchSpanProcessor(exporter))
    trace.set_tracer_provider(provider)

    RequestsInstrumentor().instrument()
    FastAPIInstrumentor.instrument_app(app)


if __name__ == "__main__":
    import uvicorn

    uvicorn.run("main:app", host="0.0.0.0", port=8000)
