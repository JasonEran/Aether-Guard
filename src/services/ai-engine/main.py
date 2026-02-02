import logging
import os
from contextlib import asynccontextmanager

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


@asynccontextmanager
async def lifespan(app_instance: FastAPI):
    configure_tracing()
    app_instance.state.scorer = scorer
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
    s_v: list[float] = Field(alias="S_v")
    p_v: float = Field(alias="P_v")
    b_s: float = Field(alias="B_s")

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
    # Placeholder semantic enrichment: use simple heuristics until NLP pipeline is online.
    combined = " ".join(
        [doc.title + " " + (doc.summary or "") for doc in payload.documents]
    ).lower()
    negative_terms = ("outage", "disruption", "degraded", "incident", "latency", "unavailable")
    has_negative = any(term in combined for term in negative_terms)

    s_v = [0.1, 0.1, 0.1]
    p_v = 0.15
    b_s = 0.0
    if has_negative:
        s_v = [0.9, 0.2, 0.1]
        p_v = 0.85
        b_s = 0.2

    return EnrichResponse(S_v=s_v, P_v=p_v, B_s=b_s)

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
