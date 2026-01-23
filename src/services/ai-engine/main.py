import logging

from fastapi import FastAPI
from pydantic import BaseModel, Field

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


if __name__ == "__main__":
    import uvicorn

    uvicorn.run("main:app", host="0.0.0.0", port=8000)
