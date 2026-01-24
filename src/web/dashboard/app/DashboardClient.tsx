'use client';

import { useEffect, useMemo, useState } from 'react';
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

const formatLocalTime = (value?: string) => {
  if (!value) {
    return '--';
  }

  const parsed = Date.parse(value);
  if (Number.isNaN(parsed)) {
    return value;
  }

  return new Date(parsed).toLocaleTimeString();
};

export default function DashboardClient({ userName, userRole }: DashboardClientProps) {
  const [agents, setAgents] = useState<FleetEntry[]>([]);
  const [history, setHistory] = useState<RiskPoint[]>([]);
  const [auditLogs, setAuditLogs] = useState<AuditLog[]>([]);
  const [usingMock, setUsingMock] = useState(false);
  const [lastUpdated, setLastUpdated] = useState<string>('');

  const summary = useMemo(() => {
    const total = agents.length;
    const critical = agents.filter((agent) => agent.riskScore > 0.8).length;
    const migrating = agents.filter((agent) => agent.status === 'MIGRATING').length;
    const failed = agents.filter((agent) => agent.status === 'FAILED').length;
    const avgRisk = total > 0 ? agents.reduce((sum, agent) => sum + agent.riskScore, 0) / total : 0;
    const tiers = agents.reduce(
      (acc, agent) => {
        acc[agent.tier] += 1;
        return acc;
      },
      { T1: 0, T2: 0, T3: 0 },
    );

    return {
      total,
      critical,
      migrating,
      failed,
      avgRisk,
      tiers,
    };
  }, [agents]);

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

      const useMockFleet = fleetData.length === 0;
      const useMockHistory = historyData.length === 0;
      const useMockAudits = auditData.length === 0;
      const useMock = useMockFleet || useMockHistory || useMockAudits;
      setUsingMock(useMock);
      const fleetSource: FleetEntry[] = useMockFleet
        ? MOCK_PAYLOAD.mockAgents
        : fleetData.map((agent) => ({
            ...agent,
            lastCheckpoint: agent.lastHeartbeat,
          }));
      setAgents(fleetSource);
      setHistory(useMockHistory ? MOCK_PAYLOAD.mockHistory : historyData);
      setAuditLogs((useMockAudits ? MOCK_PAYLOAD.mockAudits : auditData).slice(0, 12));
      setLastUpdated(new Date().toISOString());
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
    <main className="relative min-h-screen overflow-hidden bg-slate-950 text-slate-100">
      <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(circle_at_top,_rgba(14,116,144,0.2),_transparent_55%)]" />
      <div className="pointer-events-none absolute inset-0 opacity-30 [background-size:80px_80px] [background-image:linear-gradient(to_right,rgba(148,163,184,0.08)_1px,transparent_1px),linear-gradient(to_bottom,rgba(148,163,184,0.08)_1px,transparent_1px)]" />
      <div className="relative mx-auto max-w-7xl px-6 py-8">
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
              <span
                className={`flex items-center gap-2 rounded-full border px-3 py-1 uppercase tracking-[0.2em] ${
                  usingMock
                    ? 'border-amber-500/40 bg-amber-500/10 text-amber-200'
                    : 'border-emerald-500/40 bg-emerald-500/10 text-emerald-200'
                }`}
              >
                <span
                  className={`h-2 w-2 rounded-full ${
                    usingMock ? 'bg-amber-400' : 'bg-emerald-400'
                  }`}
                />
                {usingMock ? 'Simulation Mode' : 'Live Data'}
              </span>
              <span className="rounded-full border border-slate-800 bg-slate-900/60 px-3 py-1 text-slate-300">
                Last Sync: {formatLocalTime(lastUpdated)}
              </span>
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

        <section className="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <div className="rounded-2xl border border-slate-800 bg-slate-900/70 p-4 shadow-[0_0_30px_rgba(15,23,42,0.45)]">
            <div className="text-xs uppercase tracking-[0.2em] text-slate-400">Active Nodes</div>
            <div className="mt-3 text-2xl font-semibold text-slate-100">{summary.total}</div>
            <div className="mt-2 text-xs text-slate-500">
              Tier Split: T1 {summary.tiers.T1} | T2 {summary.tiers.T2} | T3 {summary.tiers.T3}
            </div>
          </div>
          <div className="rounded-2xl border border-red-500/40 bg-red-500/10 p-4 shadow-[0_0_30px_rgba(127,29,29,0.35)]">
            <div className="text-xs uppercase tracking-[0.2em] text-red-200">Critical Risk</div>
            <div className="mt-3 text-2xl font-semibold text-red-100">{summary.critical}</div>
            <div className="mt-2 text-xs text-red-200/70">Risk &gt; 0.80</div>
            <div className="mt-1 text-xs text-red-200/70">Failed nodes: {summary.failed}</div>
          </div>
          <div className="rounded-2xl border border-amber-500/40 bg-amber-500/10 p-4 shadow-[0_0_30px_rgba(120,53,15,0.35)]">
            <div className="text-xs uppercase tracking-[0.2em] text-amber-200">Active Migrations</div>
            <div className="mt-3 text-2xl font-semibold text-amber-100">{summary.migrating}</div>
            <div className="mt-2 text-xs text-amber-200/70">Live checkpoint flow</div>
          </div>
          <div className="rounded-2xl border border-slate-800 bg-slate-900/70 p-4 shadow-[0_0_30px_rgba(15,23,42,0.45)]">
            <div className="text-xs uppercase tracking-[0.2em] text-slate-400">Average Risk</div>
            <div className="mt-3 text-2xl font-semibold text-slate-100">
              {summary.avgRisk.toFixed(2)}
            </div>
            <div className="mt-2 text-xs text-slate-500">Fleet mean score</div>
          </div>
        </section>

        <section className="mt-8 grid gap-6 lg:grid-cols-[minmax(0,1.05fr)_minmax(0,1.95fr)]">
          <div className="lg:sticky lg:top-8 lg:col-span-1 lg:self-start">
            <ControlPanel agents={agents} onSimulateChaos={handleSimulateChaos} />
          </div>

          <div className="lg:col-span-1 flex flex-col gap-6">
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
