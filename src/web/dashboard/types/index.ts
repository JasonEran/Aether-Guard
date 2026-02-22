export type WorkloadTier = 'T1' | 'T2' | 'T3';

export interface Agent {
  agentId: string;
  status: string;
  tier: WorkloadTier;
  riskScore: number;
  lastHeartbeat: string;
  analysisStatus?: string;
  analysisConfidence?: number;
  predictedCpu?: number;
  rootCause?: string;
  rebalanceSignal?: boolean;
  diskAvailable?: number;
}

export interface AuditLog {
  id: string;
  action: string;
  agentId: string;
  result: string;
  error?: string;
  timestamp: string;
}

export interface ExternalSignal {
  id: number;
  source: string;
  externalId: string;
  title: string;
  summary?: string | null;
  region?: string | null;
  severity?: string | null;
  category?: string | null;
  url?: string | null;
  tags?: string | null;
  publishedAt: string;
  ingestedAt: string;
}

export interface ExternalSignalFeedState {
  name: string;
  url: string;
  lastFetchAt: string;
  lastSuccessAt?: string | null;
  failureCount: number;
  lastError?: string | null;
  lastStatusCode?: number | null;
}
