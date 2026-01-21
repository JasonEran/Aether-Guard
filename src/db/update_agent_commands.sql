CREATE TABLE IF NOT EXISTS agent_commands (
    id SERIAL PRIMARY KEY,
    agent_id UUID NOT NULL,
    command_type TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'PENDING',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_agent_commands_agent_status
    ON agent_commands (agent_id, status);
