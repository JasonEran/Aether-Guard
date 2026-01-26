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
