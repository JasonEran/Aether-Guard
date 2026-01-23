ALTER TABLE IF EXISTS agent_commands
    ADD COLUMN IF NOT EXISTS command_id UUID,
    ADD COLUMN IF NOT EXISTS workload_id TEXT,
    ADD COLUMN IF NOT EXISTS action TEXT,
    ADD COLUMN IF NOT EXISTS parameters TEXT,
    ADD COLUMN IF NOT EXISTS nonce TEXT,
    ADD COLUMN IF NOT EXISTS signature TEXT,
    ADD COLUMN IF NOT EXISTS expires_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ;

CREATE INDEX IF NOT EXISTS idx_agent_commands_command_id
    ON agent_commands (command_id);

CREATE TABLE IF NOT EXISTS command_audits (
    id SERIAL PRIMARY KEY,
    command_id UUID NOT NULL,
    actor TEXT NOT NULL,
    action TEXT NOT NULL,
    result TEXT NOT NULL,
    error TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_command_audits_command_id
    ON command_audits (command_id);
