CREATE EXTENSION IF NOT EXISTS timescaledb;

CREATE TABLE IF NOT EXISTS Agents (
    Id UUID PRIMARY KEY,
    AgentToken VARCHAR(255) NOT NULL,
    Hostname VARCHAR(255) NOT NULL,
    Status VARCHAR(50) NOT NULL,
    LastHeartbeat TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS TelemetryRecords (
    Id BIGSERIAL PRIMARY KEY,
    AgentId UUID NOT NULL,
    CpuUsage DOUBLE PRECISION NOT NULL,
    MemoryUsage DOUBLE PRECISION NOT NULL,
    "Timestamp" TIMESTAMPTZ NOT NULL
);

ALTER TABLE IF EXISTS telemetryrecords
    DROP CONSTRAINT IF EXISTS telemetryrecords_pkey;

ALTER TABLE IF EXISTS telemetryrecords
    ADD PRIMARY KEY (Id, "Timestamp");

SELECT create_hypertable(
    'TelemetryRecords',
    'Timestamp',
    if_not_exists => TRUE,
    migrate_data => TRUE,
    chunk_time_interval => INTERVAL '1 day');

SELECT add_retention_policy('TelemetryRecords', INTERVAL '90 days', if_not_exists => TRUE);

CREATE MATERIALIZED VIEW IF NOT EXISTS telemetry_hourly
WITH (timescaledb.continuous) AS
SELECT
    AgentId,
    time_bucket('1 hour', "Timestamp") AS bucket,
    AVG(CpuUsage) AS avg_cpu_usage,
    MAX(CpuUsage) AS max_cpu_usage
FROM TelemetryRecords
GROUP BY AgentId, bucket;

SELECT add_continuous_aggregate_policy('telemetry_hourly',
    start_offset => INTERVAL '7 days',
    end_offset => INTERVAL '1 hour',
    schedule_interval => INTERVAL '1 hour');
