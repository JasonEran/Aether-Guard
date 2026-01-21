export interface DashboardData {
  telemetry: {
    agentId: string;
    cpuUsage: number;
    memoryUsage: number;
    timestamp: number;
  };
  analysis?: {
    status: string;
    confidence: number;
    predictedCpu: number;
    rootCause: string;
  };
}

export interface TelemetryRecord {
  id: number;
  agentId: string;
  cpuUsage: number;
  memoryUsage: number;
  aiStatus: string;
  aiConfidence?: number;
  predictedCpu?: number;
  rootCause?: string;
  timestamp: string;
}

export async function fetchLatestTelemetry(): Promise<DashboardData | null> {
  try {
    const response = await fetch('http://localhost:5000/api/v1/dashboard/latest', {
      cache: 'no-store',
    });

    if (!response.ok) {
      return null;
    }

    const data = (await response.json()) as DashboardData;
    return data;
  } catch (error) {
    console.error('[Dashboard] Failed to fetch telemetry', error);
    return null;
  }
}

export async function fetchHistory(): Promise<TelemetryRecord[]> {
  try {
    const response = await fetch('http://localhost:5000/api/v1/dashboard/history', {
      cache: 'no-store',
    });

    if (!response.ok) {
      return [];
    }

    const data = (await response.json()) as TelemetryRecord[];
    return data;
  } catch (error) {
    console.error('[Dashboard] Failed to fetch history', error);
    return [];
  }
}
