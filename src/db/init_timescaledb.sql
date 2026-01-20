CREATE EXTENSION IF NOT EXISTS timescaledb;

CREATE TABLE IF NOT EXISTS Agents (
    Id UUID PRIMARY KEY,
    AgentToken VARCHAR(255) NOT NULL,
    Hostname VARCHAR(255) NOT NULL,
    Status VARCHAR(50) NOT NULL,
    LastHeartbeat TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS "TelemetryRecords" (
    "Id" BIGSERIAL PRIMARY KEY,
    "AgentId" TEXT NOT NULL,
    "CpuUsage" DOUBLE PRECISION NOT NULL,
    "MemoryUsage" DOUBLE PRECISION NOT NULL,
    "AiStatus" TEXT NOT NULL,
    "AiConfidence" DOUBLE PRECISION NOT NULL,
    "Timestamp" TIMESTAMPTZ NOT NULL
);

ALTER TABLE IF EXISTS "TelemetryRecords"
    DROP CONSTRAINT IF EXISTS "TelemetryRecords_pkey";

ALTER TABLE IF EXISTS "TelemetryRecords"
    ADD PRIMARY KEY ("Id", "Timestamp");

ALTER TABLE IF EXISTS "TelemetryRecords"
    ALTER COLUMN "AgentId" TYPE TEXT USING "AgentId"::text;

ALTER TABLE IF EXISTS "TelemetryRecords"
    ADD COLUMN IF NOT EXISTS "AiStatus" TEXT NOT NULL DEFAULT '';

ALTER TABLE IF EXISTS "TelemetryRecords"
    ADD COLUMN IF NOT EXISTS "AiConfidence" DOUBLE PRECISION NOT NULL DEFAULT 0;

SELECT create_hypertable(
    '"TelemetryRecords"',
    'Timestamp',
    if_not_exists => TRUE,
    migrate_data => TRUE,
    chunk_time_interval => INTERVAL '1 day');

SELECT add_retention_policy('"TelemetryRecords"', INTERVAL '90 days', if_not_exists => TRUE);

CREATE MATERIALIZED VIEW IF NOT EXISTS telemetry_hourly
WITH (timescaledb.continuous) AS
SELECT
    "AgentId",
    time_bucket('1 hour', "Timestamp") AS bucket,
    AVG("CpuUsage") AS avg_cpu_usage,
    MAX("CpuUsage") AS max_cpu_usage
FROM "TelemetryRecords"
GROUP BY "AgentId", bucket;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM timescaledb_information.jobs
        WHERE hypertable_name = 'telemetry_hourly'
          AND proc_name = 'policy_refresh_continuous_aggregate'
    ) THEN
        PERFORM add_continuous_aggregate_policy('telemetry_hourly',
            start_offset => INTERVAL '7 days',
            end_offset => INTERVAL '1 hour',
            schedule_interval => INTERVAL '1 hour');
    END IF;
END $$;
