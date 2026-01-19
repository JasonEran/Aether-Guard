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
  };
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
