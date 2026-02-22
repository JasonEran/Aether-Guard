import logging
import os
import re
import time
from contextlib import asynccontextmanager, contextmanager
from dataclasses import dataclass
from functools import lru_cache
from typing import Iterable, Iterator

from fastapi import FastAPI
from pydantic import BaseModel, Field, ConfigDict
from opentelemetry import trace, metrics
from opentelemetry.trace import Status, StatusCode
from opentelemetry.exporter.otlp.proto.http.trace_exporter import OTLPSpanExporter
from opentelemetry.exporter.otlp.proto.http.metric_exporter import OTLPMetricExporter
from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor
from opentelemetry.instrumentation.requests import RequestsInstrumentor
from opentelemetry.sdk.resources import Resource
from opentelemetry.sdk.metrics import MeterProvider
from opentelemetry.sdk.metrics.export import PeriodicExportingMetricReader
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor

from model import RiskScorer

logger = logging.getLogger("uvicorn.error")
scorer = RiskScorer()
ENRICHMENT_SCHEMA_VERSION = "1.0"
SUMMARY_SCHEMA_VERSION = "1.0"

DEFAULT_FINBERT_MODEL = os.getenv("AI_FINBERT_MODEL", "ProsusAI/finbert")
ENRICHMENT_PROVIDER = os.getenv("AI_ENRICH_PROVIDER", "finbert").lower()
ENRICHMENT_MAX_CHARS = int(os.getenv("AI_ENRICH_MAX_CHARS", "2000"))
ENRICHMENT_CACHE_SIZE = int(os.getenv("AI_ENRICH_CACHE_SIZE", "1024"))
SUMMARY_PROVIDER = os.getenv("AI_SUMMARIZER_PROVIDER", "heuristic").lower()
SUMMARY_ENDPOINT = os.getenv("AI_SUMMARIZER_ENDPOINT", "")
SUMMARY_MAX_CHARS = int(os.getenv("AI_SUMMARIZER_MAX_CHARS", "600"))
SUMMARY_CACHE_SIZE = int(os.getenv("AI_SUMMARIZER_CACHE_SIZE", "1024"))
SUMMARY_TIMEOUT_SECONDS = float(os.getenv("AI_SUMMARIZER_TIMEOUT", "8"))
SIGNALS_TELEMETRY_METER = "aether_guard.ai.signals"
SIGNALS_TELEMETRY_TRACER = "aether_guard.ai.signals"

signals_tracer = trace.get_tracer(SIGNALS_TELEMETRY_TRACER)
signals_request_counter = None
signals_error_counter = None
signals_latency_histogram = None
signals_document_histogram = None


@asynccontextmanager
async def lifespan(app_instance: FastAPI):
    configure_tracing()
    app_instance.state.scorer = scorer
    app_instance.state.enricher = build_enricher()
    app_instance.state.summarizer = build_summarizer()
    logger.info("AI Engine Online.")
    yield


app = FastAPI(lifespan=lifespan)


def initialize_signals_metrics() -> None:
    global signals_request_counter
    global signals_error_counter
    global signals_latency_histogram
    global signals_document_histogram

    meter = metrics.get_meter(SIGNALS_TELEMETRY_METER)
    signals_request_counter = meter.create_counter(
        "aetherguard.ai.signals.requests",
        description="Count of AI signal API requests.",
    )
    signals_error_counter = meter.create_counter(
        "aetherguard.ai.signals.errors",
        description="Count of AI signal API request failures.",
    )
    signals_latency_histogram = meter.create_histogram(
        "aetherguard.ai.signals.duration.ms",
        unit="ms",
        description="Latency of AI signal API requests.",
    )
    signals_document_histogram = meter.create_histogram(
        "aetherguard.ai.signals.documents",
        unit="documents",
        description="Document count per AI signal API request.",
    )


def record_signal_request(
    endpoint: str,
    provider: str,
    documents: int,
    duration_ms: float,
    outcome: str,
    error_type: str | None = None,
) -> None:
    base_attributes = {
        "endpoint": endpoint,
        "provider": provider,
    }
    outcome_attributes = {
        **base_attributes,
        "outcome": outcome,
    }

    if signals_request_counter is not None:
        signals_request_counter.add(1, outcome_attributes)
    if signals_latency_histogram is not None:
        signals_latency_histogram.record(duration_ms, base_attributes)
    if signals_document_histogram is not None:
        signals_document_histogram.record(max(0, documents), base_attributes)

    if error_type and signals_error_counter is not None:
        signals_error_counter.add(1, {**base_attributes, "error.type": error_type})


@contextmanager
def observe_signal_endpoint(endpoint: str, provider: str, documents: int) -> Iterator[object]:
    start = time.perf_counter()
    outcome = "success"
    error_type: str | None = None

    with signals_tracer.start_as_current_span(f"ai{endpoint.replace('/', '.')}") as span:
        span.set_attribute("ai.signals.endpoint", endpoint)
        span.set_attribute("ai.signals.provider", provider)
        span.set_attribute("ai.signals.documents", documents)
        try:
            yield span
            span.set_status(Status(StatusCode.OK))
        except Exception as exc:
            outcome = "error"
            error_type = type(exc).__name__
            span.record_exception(exc)
            span.set_status(Status(StatusCode.ERROR))
            raise
        finally:
            duration_ms = (time.perf_counter() - start) * 1000
            record_signal_request(endpoint, provider, documents, duration_ms, outcome, error_type)


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
        min_length=3,
        max_length=3,
        description="Sentiment vector [negative, neutral, positive], normalized to sum to 1.",
    )
    p_v: float = Field(
        alias="P_v",
        ge=0.0,
        le=1.0,
        description="Volatility probability in the range [0, 1].",
    )
    b_s: float = Field(
        alias="B_s",
        ge=0.0,
        le=1.0,
        description="Supply or capacity bias (long-horizon signal).",
    )

    model_config = ConfigDict(populate_by_name=True)


class EnrichBatchItem(BaseModel):
    index: int = Field(ge=0)
    s_v: list[float] = Field(
        alias="S_v",
        min_length=3,
        max_length=3,
        description="Sentiment vector [negative, neutral, positive], normalized to sum to 1.",
    )
    p_v: float = Field(
        alias="P_v",
        ge=0.0,
        le=1.0,
        description="Volatility probability in the range [0, 1].",
    )
    b_s: float = Field(
        alias="B_s",
        ge=0.0,
        le=1.0,
        description="Supply or capacity bias (long-horizon signal).",
    )

    model_config = ConfigDict(populate_by_name=True)


class EnrichBatchResponse(BaseModel):
    schema_version: str = Field(alias="schemaVersion", description="Semantic vector schema version.")
    vectors: list[EnrichBatchItem]

    model_config = ConfigDict(populate_by_name=True)


class SummarizeRequest(BaseModel):
    documents: list[SignalDocument]
    max_chars: int | None = Field(default=None, alias="maxChars")

    model_config = ConfigDict(populate_by_name=True)


class SummaryItem(BaseModel):
    index: int
    source: str
    title: str
    summary: str
    truncated: bool


class SummarizeResponse(BaseModel):
    schema_version: str = Field(alias="schemaVersion")
    summaries: list[SummaryItem]

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
    with observe_signal_endpoint("/signals/enrich", ENRICHMENT_PROVIDER, len(payload.documents)) as span:
        result = sanitize_enrich_result(enricher.enrich(payload.documents))
        span.set_attribute("ai.signals.schema_version", ENRICHMENT_SCHEMA_VERSION)
        return EnrichResponse(
            schemaVersion=ENRICHMENT_SCHEMA_VERSION,
            S_v=result.s_v,
            P_v=result.p_v,
            B_s=result.b_s,
        )


@app.post("/signals/enrich/batch", response_model=EnrichBatchResponse)
def enrich_signals_batch(payload: EnrichRequest) -> EnrichBatchResponse:
    enricher: SemanticEnricher = app.state.enricher
    with observe_signal_endpoint("/signals/enrich/batch", ENRICHMENT_PROVIDER, len(payload.documents)) as span:
        vectors = [
            EnrichBatchItem(
                index=index,
                S_v=result.s_v,
                P_v=result.p_v,
                B_s=result.b_s,
            )
            for index, result in enumerate(
                [sanitize_enrich_result(result) for result in enricher.enrich_batch(payload.documents)]
            )
        ]

        span.set_attribute("ai.signals.schema_version", ENRICHMENT_SCHEMA_VERSION)
        span.set_attribute("ai.signals.vectors", len(vectors))
        return EnrichBatchResponse(
            schemaVersion=ENRICHMENT_SCHEMA_VERSION,
            vectors=vectors,
        )


@app.get("/signals/enrich/schema")
def enrich_schema() -> dict:
    return {
        "schemaVersion": ENRICHMENT_SCHEMA_VERSION,
        "batchEndpoint": "/signals/enrich/batch",
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

    def enrich_batch(self, documents: Iterable[SignalDocument]) -> list[EnrichResult]:
        return [self.enrich([document]) for document in documents]


@dataclass
class SummarizeResult:
    summary: str
    truncated: bool


class SignalSummarizer:
    def summarize(self, text: str, max_chars: int) -> SummarizeResult:  # pragma: no cover - interface
        raise NotImplementedError


class HeuristicEnricher(SemanticEnricher):
    negative_terms = ("outage", "disruption", "degraded", "incident", "latency", "unavailable")
    supply_terms = ("capacity", "shortage", "procurement", "inventory", "supply", "quota")

    def enrich(self, documents: Iterable[SignalDocument]) -> EnrichResult:
        document_list = list(documents)
        if not document_list:
            return EnrichResult(s_v=[0.15, 0.7, 0.15], p_v=0.1, b_s=0.0)

        # Keep the legacy aggregate behavior for backward compatibility.
        combined = " ".join([self._doc_text(doc) for doc in document_list]).lower()
        return self._score_text(combined)

    def enrich_batch(self, documents: Iterable[SignalDocument]) -> list[EnrichResult]:
        return [self._score_text(self._doc_text(document).lower()) for document in documents]

    def _score_text(self, combined: str) -> EnrichResult:
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
        document_list = list(documents)
        scores = [self._cache(self._doc_text(doc)) for doc in document_list]
        if not scores:
            return EnrichResult(s_v=[0.15, 0.7, 0.15], p_v=0.1, b_s=0.0)

        neg = sum(score[0] for score in scores) / len(scores)
        neutral = sum(score[1] for score in scores) / len(scores)
        pos = sum(score[2] for score in scores) / len(scores)
        s_v = normalize_vector([neg, neutral, pos])

        volatility_boost = max(0.0, neg - pos)
        p_v = clamp(0.1 + volatility_boost * 1.2, 0.0, 1.0)
        b_s = clamp(self._supply_bias(document_list), 0.0, 1.0)
        return EnrichResult(s_v=s_v, p_v=p_v, b_s=b_s)

    def enrich_batch(self, documents: Iterable[SignalDocument]) -> list[EnrichResult]:
        results: list[EnrichResult] = []
        for document in documents:
            scores = self._cache(self._doc_text(document))
            neg = scores[0]
            pos = scores[2]
            p_v = clamp(0.1 + max(0.0, neg - pos) * 1.2, 0.0, 1.0)
            b_s = clamp(self._supply_bias([document]), 0.0, 1.0)
            results.append(EnrichResult(s_v=scores, p_v=p_v, b_s=b_s))
        return results

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


class HeuristicSummarizer(SignalSummarizer):
    def __init__(self, max_chars: int, cache_size: int) -> None:
        self._max_chars = max_chars
        self._cache = lru_cache(maxsize=cache_size)(self._summarize_text)

    def summarize(self, text: str, max_chars: int | None = None) -> SummarizeResult:
        limit = max_chars or self._max_chars
        clean = normalize_text(text)
        if not clean:
            return SummarizeResult(summary="", truncated=False)
        if limit <= 0:
            return SummarizeResult(summary="", truncated=len(clean) > 0)
        return self._cache(clean, limit)

    def _summarize_text(self, text: str, limit: int) -> SummarizeResult:
        if len(text) <= limit:
            return SummarizeResult(summary=text, truncated=False)

        sentences = re.split(r"(?<=[.!?])\s+", text)
        summary_parts: list[str] = []
        total_len = 0
        for sentence in sentences:
            if not sentence:
                continue
            next_len = total_len + len(sentence) + (1 if summary_parts else 0)
            if next_len > limit:
                break
            summary_parts.append(sentence)
            total_len = next_len

        if not summary_parts:
            return SummarizeResult(summary=text[:limit].rstrip(), truncated=True)

        summary = " ".join(summary_parts).rstrip()
        return SummarizeResult(summary=summary, truncated=True)


class HttpSummarizer(SignalSummarizer):
    def __init__(self, endpoint: str, fallback: SignalSummarizer, max_chars: int, cache_size: int, timeout: float) -> None:
        self._endpoint = endpoint
        self._fallback = fallback
        self._max_chars = max_chars
        self._timeout = timeout
        self._cache = lru_cache(maxsize=cache_size)(self._summarize_remote)

    def summarize(self, text: str, max_chars: int | None = None) -> SummarizeResult:
        limit = max_chars or self._max_chars
        clean = normalize_text(text)
        if not clean:
            return SummarizeResult(summary="", truncated=False)
        if limit <= 0:
            return SummarizeResult(summary="", truncated=len(clean) > 0)
        return self._cache(clean, limit)

    def _summarize_remote(self, text: str, limit: int) -> SummarizeResult:
        try:
            import requests

            response = requests.post(
                self._endpoint,
                json={"text": text, "maxChars": limit},
                timeout=self._timeout,
            )
            response.raise_for_status()
            payload = response.json()
            summary = str(payload.get("summary", "")).strip()
            if not summary:
                return self._fallback.summarize(text, limit)
            return SummarizeResult(summary=summary, truncated=len(text) > len(summary))
        except Exception as exc:
            logger.warning("Summarizer remote call failed, falling back: %s", exc)
            return self._fallback.summarize(text, limit)


def build_summarizer() -> SignalSummarizer:
    heuristic = HeuristicSummarizer(max_chars=SUMMARY_MAX_CHARS, cache_size=SUMMARY_CACHE_SIZE)

    if SUMMARY_PROVIDER == "http":
        if SUMMARY_ENDPOINT:
            return HttpSummarizer(
                endpoint=SUMMARY_ENDPOINT,
                fallback=heuristic,
                max_chars=SUMMARY_MAX_CHARS,
                cache_size=SUMMARY_CACHE_SIZE,
                timeout=SUMMARY_TIMEOUT_SECONDS,
            )
        logger.warning("AI_SUMMARIZER_PROVIDER=http set but AI_SUMMARIZER_ENDPOINT is empty; using heuristic.")

    return heuristic


def normalize_vector(values: list[float]) -> list[float]:
    total = sum(max(0.0, value) for value in values)
    if total <= 0:
        return [0.15, 0.7, 0.15]
    return [max(0.0, value) / total for value in values]


def clamp(value: float, min_value: float, max_value: float) -> float:
    return max(min_value, min(max_value, value))


def sanitize_enrich_result(result: EnrichResult) -> EnrichResult:
    values = normalize_vector((result.s_v + [0.0, 0.0, 0.0])[:3])
    return EnrichResult(
        s_v=values,
        p_v=clamp(result.p_v, 0.0, 1.0),
        b_s=clamp(result.b_s, 0.0, 1.0),
    )


def normalize_text(text: str) -> str:
    return " ".join(text.replace("\n", " ").split()).strip()


@app.post("/signals/summarize", response_model=SummarizeResponse)
def summarize_signals(payload: SummarizeRequest) -> SummarizeResponse:
    summarizer: SignalSummarizer = app.state.summarizer
    with observe_signal_endpoint("/signals/summarize", SUMMARY_PROVIDER, len(payload.documents)) as span:
        max_chars = payload.max_chars if payload.max_chars and payload.max_chars > 0 else SUMMARY_MAX_CHARS
        summaries: list[SummaryItem] = []

        for index, doc in enumerate(payload.documents):
            source = doc.source
            title = doc.title
            text = f"{doc.title}. {doc.summary}" if doc.summary else doc.title
            result = summarizer.summarize(text, max_chars)
            summaries.append(
                SummaryItem(
                    index=index,
                    source=source,
                    title=title,
                    summary=result.summary,
                    truncated=result.truncated,
                )
            )

        span.set_attribute("ai.signals.schema_version", SUMMARY_SCHEMA_VERSION)
        span.set_attribute("ai.signals.max_chars", max_chars)
        return SummarizeResponse(schemaVersion=SUMMARY_SCHEMA_VERSION, summaries=summaries)

def configure_tracing() -> None:
    endpoint = os.getenv("OTEL_EXPORTER_OTLP_ENDPOINT")
    service_name = os.getenv("OTEL_SERVICE_NAME", "aether-guard-ai")
    resource = Resource.create({"service.name": service_name})

    if endpoint:
        trace_provider = TracerProvider(resource=resource)
        span_exporter = OTLPSpanExporter(endpoint=endpoint)
        trace_provider.add_span_processor(BatchSpanProcessor(span_exporter))
        trace.set_tracer_provider(trace_provider)

        metric_exporter = OTLPMetricExporter(endpoint=endpoint)
        metric_reader = PeriodicExportingMetricReader(metric_exporter)
        metric_provider = MeterProvider(resource=resource, metric_readers=[metric_reader])
        metrics.set_meter_provider(metric_provider)

    initialize_signals_metrics()
    RequestsInstrumentor().instrument()
    FastAPIInstrumentor.instrument_app(app)


if __name__ == "__main__":
    import uvicorn

    uvicorn.run("main:app", host="0.0.0.0", port=8000)
