'use client';

import { useEffect, useState } from 'react';
import { signOut } from 'next-auth/react';

import AuditLogStream from '../components/AuditLogStream';
import ControlPanel from '../components/ControlPanel';
import HistoryChart from '../components/HistoryChart';
import { fetchAuditLogs, fetchFleetStatus, fetchRiskHistory, sendChaosSignal, RiskPoint } from '../lib/api';
import type { Agent, AuditLog } from '../types';

interface DashboardClientProps {
  userName: string;
  userRole: string;
}

type FleetEntry = Agent & { lastCheckpoint?: string };

const MOCK_PAYLOAD = (() => {
  const baseTime = Date.parse('2025-01-01T00:00:00.000Z');
  const timestamps = Array.from({ length: 12 }, (_, index) => new Date(baseTime - (11 - index) * 60_000));
  const mockHistory: RiskPoint[] = timestamps.map((timestamp, index) => ({
    timestamp: timestamp.toISOString(),
    riskScore: index < 5 ? 0.35 : index < 9 ? 0.62 : 0.86,
  }));

  const mockAgents: FleetEntry[] = [
    {
      agentId: 'node-atlas-01',
      status: 'IDLE',
      tier: 'T1',
      riskScore: 0.18,
      lastHeartbeat: new Date(baseTime - 45_000).toISOString(),
      lastCheckpoint: new Date(baseTime - 12 * 60_000).toISOString(),
    },
    {
      agentId: 'node-zephyr-07',
      status: 'MIGRATING',
      tier: 'T2',
      riskScore: 0.87,
      lastHeartbeat: new Date(baseTime - 30_000).toISOString(),
      lastCheckpoint: new Date(baseTime - 2 * 60_000).toISOString(),
    },
    {
      agentId: 'node-sigma-12',
      status: 'FAILED',
      tier: 'T3',
      riskScore: 0.95,
      lastHeartbeat: new Date(baseTime - 8 * 60_000).toISOString(),
      lastCheckpoint: new Date(baseTime - 30 * 60_000).toISOString(),
    },
  ];

  const mockAudits: AuditLog[] = [
    {
      id: 'audit-01',
      action: 'Migration Completed',
      agentId: 'node-zephyr-07',
      result: 'Restored on node-atlas-01',
      timestamp: new Date(baseTime - 90_000).toISOString(),
    },
    {
      id: 'audit-02',
      action: 'Checkpoint Created',
      agentId: 'node-zephyr-07',
      result: 'Snapshot stored in relay vault',
      timestamp: new Date(baseTime - 2 * 60_000).toISOString(),
    },
    {
      id: 'audit-03',
      action: 'Risk Scan Updated',
      agentId: 'node-sigma-12',
      result: 'Priority raised to CRITICAL',
      timestamp: new Date(baseTime - 4 * 60_000).toISOString(),
    },
  ];

  return { mockHistory, mockAgents, mockAudits };
})();

export default function DashboardClient({ userName, userRole }: DashboardClientProps) {
  const [agents, setAgents] = useState<FleetEntry[]>([]);
  const [history, setHistory] = useState<RiskPoint[]>([]);
  const [auditLogs, setAuditLogs] = useState<AuditLog[]>([]);
  const [usingMock, setUsingMock] = useState(false);

  useEffect(() => {
    let isMounted = true;

    const load = async () => {
      const [fleetData, historyData, auditData] = await Promise.all([
        fetchFleetStatus(),
        fetchRiskHistory(),
        fetchAuditLogs(),
      ]);

      if (!isMounted) {
        return;
      }

      const useMock = fleetData.length === 0 || historyData.length === 0 || auditData.length === 0;
      setUsingMock(useMock);
      const fleetSource: FleetEntry[] = fleetData.length
        ? fleetData.map((agent) => ({
            ...agent,
            lastCheckpoint: agent.lastHeartbeat,
          }))
        : MOCK_PAYLOAD.mockAgents;
      setAgents(fleetSource);
      setHistory(historyData.length ? historyData : MOCK_PAYLOAD.mockHistory);
      const auditPool = (auditData.length ? auditData : MOCK_PAYLOAD.mockAudits).slice(0, 12);
      const hasMigration = auditPool.some((log) => log.action === 'Migration Completed');
      setAuditLogs(
        hasMigration
          ? auditPool
          : [MOCK_PAYLOAD.mockAudits[0], ...auditPool].slice(0, 12),
      );
    };

    load();
    const intervalId = setInterval(load, 5000);

    return () => {
      isMounted = false;
      clearInterval(intervalId);
    };
  }, []);

  const handleSimulateChaos = async () => {
    await sendChaosSignal();
    const timestamp = new Date().toISOString();

    setAgents((prev) =>
      prev.map((agent) =>
        agent.tier === 'T2'
          ? { ...agent, status: 'MIGRATING', riskScore: 0.92, lastHeartbeat: timestamp }
          : agent,
      ),
    );

    setHistory((prev) =>
      prev.map((point, index) =>
        index > prev.length - 4
          ? { ...point, riskScore: Math.min(1, point.riskScore + 0.2) }
          : point,
      ),
    );

    setAuditLogs((prev) => [
      {
        id: `audit-${Date.now()}`,
        action: 'Rebalance Signal Injected',
        agentId: 'control-plane',
        result: 'Chaos simulation engaged',
        timestamp,
      },
      ...prev,
    ]);
  };

  if (agents.length === 0) {
    return (
      <main className="min-h-screen bg-slate-950 text-slate-100 flex items-center justify-center">
        <div className="text-center">
          <div className="text-2xl font-semibold">Aether-Guard Mission Control</div>
          <div className="mt-4 text-slate-400">Warming up orchestration grid...</div>
        </div>
      </main>
    );
  }

  return (
    <main className="min-h-screen bg-slate-950 text-slate-100">
      <div className="mx-auto max-w-6xl px-6 py-8">
        <header className="flex flex-wrap items-center justify-between gap-4">
          <div>
            <div className="flex items-center gap-3 text-sm uppercase tracking-[0.3em] text-slate-500">
              Aether-Guard
              <span className="h-1 w-10 rounded-full bg-emerald-400/70" />
              Mission Control
            </div>
            <h1 className="mt-3 text-3xl font-semibold text-slate-100">
              FinOps Migration Command Center
            </h1>
            <div className="mt-2 flex flex-wrap items-center gap-3 text-xs text-slate-400">
              <span>User: {userName}</span>
              <span>Role: {userRole}</span>
              {usingMock && (
                <span className="rounded-full border border-amber-500/40 bg-amber-500/10 px-3 py-1 text-amber-200">
                  Mock Data
                </span>
              )}
            </div>
          </div>
          <button
            type="button"
            onClick={() => signOut({ callbackUrl: '/login' })}
            className="rounded-lg border border-slate-700 px-4 py-2 text-xs uppercase tracking-[0.2em] text-slate-200 transition hover:border-emerald-500 hover:text-emerald-200"
          >
            Sign Out
          </button>
        </header>

        <section className="mt-8 grid gap-6 lg:grid-cols-3">
          <div className="lg:col-span-1">
            <ControlPanel agents={agents} onSimulateChaos={handleSimulateChaos} />
          </div>

          <div className="lg:col-span-2 flex flex-col gap-6">
            <div className="rounded-2xl border border-slate-800 bg-slate-900/70 p-6 shadow-[0_0_40px_rgba(15,23,42,0.6)]">
              <div className="text-xs uppercase tracking-[0.2em] text-slate-400">Risk Trend</div>
              <div className="mt-4">
                <HistoryChart data={history} />
              </div>
            </div>

            <AuditLogStream logs={auditLogs} />
          </div>
        </section>
      </div>
    </main>
  );
}
