import logging
import os

from fastapi import FastAPI
from pydantic import BaseModel, Field
from opentelemetry import trace
from opentelemetry.exporter.otlp.proto.http.trace_exporter import OTLPSpanExporter
from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor
from opentelemetry.instrumentation.requests import RequestsInstrumentor
from opentelemetry.sdk.resources import Resource
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor

from model import RiskScorer

logger = logging.getLogger("uvicorn.error")
app = FastAPI()
scorer = RiskScorer()


class RiskPayload(BaseModel):
    spot_price_history: list[float] = Field(default_factory=list, alias="spotPriceHistory")
    rebalance_signal: bool = Field(alias="rebalanceSignal")
    capacity_score: float = Field(alias="capacityScore")

    class Config:
        allow_population_by_field_name = True


@app.on_event("startup")
def load_model() -> None:
    configure_tracing()
    app.state.scorer = scorer
    logger.info("AI Engine Online.")


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
