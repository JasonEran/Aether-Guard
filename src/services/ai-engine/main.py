from fastapi import FastAPI
from pydantic import BaseModel

app = FastAPI()


class Telemetry(BaseModel):
    cpuUsage: float
    memoryUsage: float


@app.get("/")
def root():
    return {"status": "AI Engine Online"}


@app.post("/analyze")
def analyze(payload: Telemetry):
    if payload.cpuUsage > 90.0 or payload.memoryUsage > 80.0:
        return {"status": "Critical", "confidence": 0.95}

    return {"status": "Normal", "confidence": 0.99}


if __name__ == "__main__":
    import uvicorn

    uvicorn.run("main:app", host="0.0.0.0", port=8000)
