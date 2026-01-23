import type { Agent, AuditLog } from '../types';

export interface RiskPoint {
  timestamp: string;
  riskScore: number;
}

interface CoreLatestResponse {
  telemetry?: {
    agentId: string;
    workloadTier: string;
    rebalanceSignal: boolean;
    diskAvailable: number;
    timestamp: number;
  };
  analysis?: {
    status: string;
    confidence: number;
    predictedCpu: number;
    rootCause: string;
  };
}

interface CoreHistoryRecord {
  id: number;
  agentId: string;
  workloadTier: string;
  rebalanceSignal: boolean;
  diskAvailable: number;
  aiStatus: string;
  aiConfidence?: number;
  predictedCpu?: number;
  rootCause?: string | null;
  timestamp: string;
}

interface CoreAuditRecord {
  id: number;
  commandId: string;
  actor: string;
  action: string;
  result: string;
  error: string;
  createdAt: string;
}

const normalizeRiskScore = (value: unknown): number => {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return 0;
  }

  const normalized = value > 1 ? value / 100 : value;
  return Math.min(1, Math.max(0, normalized));
};

const normalizeTier = (value: string | undefined): Agent['tier'] => {
  if (value === 'T1' || value === 'T2' || value === 'T3') {
    return value;
  }

  return 'T2';
};

const toIsoTimestamp = (value: number | string): string => {
  if (typeof value === 'number') {
    return new Date(value * 1000).toISOString();
  }

  const parsed = Date.parse(value);
  return Number.isNaN(parsed) ? new Date().toISOString() : new Date(parsed).toISOString();
};

export async function fetchFleetStatus(): Promise<Agent[]> {
  try {
    const response = await fetch('/api/dashboard/latest', { cache: 'no-store' });
    if (!response.ok) {
      return [];
    }

    const data = (await response.json()) as CoreLatestResponse;
    if (!data?.telemetry) {
      return [];
    }

    const telemetry = data.telemetry;
    const riskScore = normalizeRiskScore(data.analysis?.confidence ?? (telemetry.rebalanceSignal ? 0.92 : 0.28));
    const status = telemetry.rebalanceSignal ? 'MIGRATING' : 'IDLE';

    return [
      {
        agentId: telemetry.agentId,
        status,
        tier: normalizeTier(telemetry.workloadTier),
        riskScore,
        lastHeartbeat: toIsoTimestamp(telemetry.timestamp),
      },
    ];
  } catch (error) {
    console.error('[Dashboard] Failed to fetch fleet status', error);
    return [];
  }
}

export async function fetchRiskHistory(): Promise<RiskPoint[]> {
  try {
    const response = await fetch('/api/dashboard/history', { cache: 'no-store' });
    if (!response.ok) {
      return [];
    }

    const data = (await response.json()) as CoreHistoryRecord[];
    return data.map((record) => ({
      timestamp: toIsoTimestamp(record.timestamp),
      riskScore: normalizeRiskScore(record.aiConfidence ?? 0),
    }));
  } catch (error) {
    console.error('[Dashboard] Failed to fetch risk history', error);
    return [];
  }
}

export async function fetchAuditLogs(): Promise<AuditLog[]> {
  try {
    const response = await fetch('/api/audits', { cache: 'no-store' });
    if (!response.ok) {
      return [];
    }

    const data = (await response.json()) as CoreAuditRecord[];
    return data.map((record) => ({
      id: record.id.toString(),
      action: record.action,
      agentId: record.actor,
      result: record.result,
      timestamp: record.createdAt,
    }));
  } catch (error) {
    console.error('[Dashboard] Failed to fetch audits', error);
    return [];
  }
}

export async function sendChaosSignal(): Promise<void> {
  const response = await fetch('/api/chaos', { method: 'POST' });
  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || 'Chaos signal failed.');
  }
}
