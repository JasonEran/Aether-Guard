# syntax=docker/dockerfile:1.5
FROM python:3.10-slim

WORKDIR /app

COPY requirements.txt ./
RUN --mount=type=cache,id=aether-guard-pip,target=/root/.cache/pip pip install -r requirements.txt

COPY model.py init_model.py ./
RUN python init_model.py

COPY main.py ./

EXPOSE 8000

CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8000"]
