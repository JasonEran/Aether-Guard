CREATE EXTENSION IF NOT EXISTS timescaledb;

CREATE TABLE IF NOT EXISTS agents (
    id UUID PRIMARY KEY,
    agenttoken VARCHAR(255) NOT NULL,
    hostname VARCHAR(255) NOT NULL,
    status VARCHAR(50) NOT NULL,
    lastheartbeat TIMESTAMPTZ NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS agents_agenttoken_key
    ON agents (agenttoken);

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_schema = 'public'
          AND table_name = 'Agents'
    ) THEN
        IF (SELECT COUNT(*) FROM agents) = 0
           AND (SELECT COUNT(*) FROM "Agents") > 0 THEN
            INSERT INTO agents (id, agenttoken, hostname, status, lastheartbeat)
            SELECT id, agenttoken, hostname, status, lastheartbeat
            FROM "Agents";
        END IF;
    END IF;
END $$;

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

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_schema = 'public'
          AND table_name = 'telemetryrecords'
    ) AND EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_schema = 'public'
          AND table_name = 'TelemetryRecords'
    ) THEN
        IF (SELECT COUNT(*) FROM "TelemetryRecords") = 0
           AND (SELECT COUNT(*) FROM telemetryrecords) > 0 THEN
            INSERT INTO "TelemetryRecords" ("AgentId", "CpuUsage", "MemoryUsage", "AiStatus", "AiConfidence", "Timestamp")
            SELECT
                telemetryrecords.agentid::text,
                telemetryrecords.cpuusage,
                telemetryrecords.memoryusage,
                ''::text,
                0::double precision,
                telemetryrecords."Timestamp"
            FROM telemetryrecords;
        END IF;
    END IF;
END $$;

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
