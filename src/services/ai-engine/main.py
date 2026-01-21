import logging
from pathlib import Path
from typing import List

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel

from model import LSTMPredictor

logger = logging.getLogger("uvicorn.error")
app = FastAPI()
MODEL_PATH = Path(__file__).resolve().parent / "model.pth"


class TelemetryItem(BaseModel):
    cpuUsage: float
    memoryUsage: float
    timestamp: int | None = None


@app.on_event("startup")
def load_model() -> None:
    model = LSTMPredictor()
    if not MODEL_PATH.exists():
        raise RuntimeError("model.pth not found. Run init_model.py to generate initial weights.")
    logger.info("Loading LSTM model from %s...", MODEL_PATH.name)
    model.load(MODEL_PATH)
    logger.info("Model loaded successfully.")
    app.state.model = model
    logger.info("AI Engine Online.")


@app.get("/")
def root() -> dict:
    return {"status": "AI Engine Online"}


@app.post("/analyze")
def analyze(payload: List[TelemetryItem] | TelemetryItem) -> dict:
    if isinstance(payload, TelemetryItem):
        payload = [payload]

    if not payload:
        raise HTTPException(status_code=400, detail="Telemetry sequence is required.")

    model: LSTMPredictor = app.state.model
    sequence = [[item.cpuUsage, item.memoryUsage] for item in payload]
    prediction = model.predict(sequence)

    last_memory = payload[-1].memoryUsage
    status = "Critical" if prediction > 85.0 else "Normal"
    confidence = 0.95 if status == "Critical" else 0.99
    rca = "No anomaly detected"

    if prediction > 85.0 and last_memory > 80.0:
        rca = "Potential Memory Leak detected"
    elif prediction > 85.0 and last_memory < 30.0:
        rca = "Compute Bound Process detected"

    return {
        "status": status,
        "prediction": prediction,
        "rca": rca,
        "confidence": confidence,
    }


if __name__ == "__main__":
    import uvicorn

    uvicorn.run("main:app", host="0.0.0.0", port=8000)
