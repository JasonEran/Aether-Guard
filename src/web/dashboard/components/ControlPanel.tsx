'use client';

import { useState } from 'react';

import type { Agent } from '../types';

type FleetEntry = Agent & { lastCheckpoint?: string };

interface ControlPanelProps {
  agents: FleetEntry[];
  onSimulateChaos?: () => Promise<void>;
}

const statusStyles: Record<string, string> = {
  IDLE: 'border-emerald-500/40 bg-emerald-500/15 text-emerald-300',
  PREPARING: 'border-sky-500/40 bg-sky-500/15 text-sky-200',
  CHECKPOINTING: 'border-amber-500/40 bg-amber-500/15 text-amber-200',
  TRANSFERRING: 'border-amber-500/40 bg-amber-500/15 text-amber-200',
  RESTORING: 'border-sky-500/40 bg-sky-500/15 text-sky-200',
  MIGRATING: 'border-amber-500/40 bg-amber-500/15 text-amber-300',
  ONLINE: 'border-emerald-500/40 bg-emerald-500/15 text-emerald-300',
  OFFLINE: 'border-slate-600 bg-slate-800 text-slate-300',
  FAILED: 'border-red-500/40 bg-red-500/15 text-red-300',
};

const tierStyles: Record<Agent['tier'], string> = {
  T1: 'border-emerald-500/40 bg-emerald-500/10 text-emerald-200',
  T2: 'border-cyan-500/40 bg-cyan-500/10 text-cyan-200',
  T3: 'border-rose-500/40 bg-rose-500/10 text-rose-200',
};

const formatTimestamp = (value?: string) => {
  if (!value) {
    return '--';
  }

  const parsed = Date.parse(value);
  if (Number.isNaN(parsed)) {
    return value;
  }

  return new Date(parsed).toLocaleTimeString();
};

export default function ControlPanel({ agents, onSimulateChaos }: ControlPanelProps) {
  const [chaosStatus, setChaosStatus] = useState<'idle' | 'loading' | 'success' | 'error'>('idle');
  const [message, setMessage] = useState<string>('');

  const handleChaos = async () => {
    if (!onSimulateChaos) {
      return;
    }

    setChaosStatus('loading');
    setMessage('Injecting rebalance signal...');

    try {
      await onSimulateChaos();
      setChaosStatus('success');
      setMessage('Chaos signal accepted by control plane.');
    } catch (error) {
      setChaosStatus('error');
      setMessage(error instanceof Error ? error.message : 'Chaos signal failed.');
    }

    setTimeout(() => {
      setChaosStatus('idle');
      setMessage('');
    }, 4000);
  };

  return (
    <div className="rounded-2xl border border-slate-800 bg-slate-900/70 p-6 shadow-[0_0_40px_rgba(15,23,42,0.6)]">
      <div className="flex items-center justify-between gap-4">
        <div>
          <div className="text-xs uppercase tracking-[0.2em] text-slate-400">Fleet Status</div>
          <div className="mt-2 text-lg font-semibold text-slate-100">Migration Control Grid</div>
        </div>
        <button
          type="button"
          onClick={handleChaos}
          disabled={chaosStatus === 'loading'}
          className="rounded-lg border border-amber-500/60 bg-amber-500/20 px-4 py-2 text-xs font-semibold uppercase tracking-[0.2em] text-amber-200 transition hover:border-amber-400 hover:bg-amber-500/30 disabled:cursor-not-allowed disabled:border-slate-700 disabled:bg-slate-800"
        >
          {chaosStatus === 'loading' ? 'Signaling...' : 'Simulate Chaos'}
        </button>
      </div>

      <div className="mt-5 overflow-hidden rounded-xl border border-slate-800">
        <table className="w-full text-left text-sm text-slate-200">
          <thead className="bg-slate-900/80 text-xs uppercase tracking-[0.2em] text-slate-400">
            <tr>
              <th className="px-4 py-3">Agent ID</th>
              <th className="px-4 py-3">Workload Tier</th>
              <th className="px-4 py-3">Current State</th>
              <th className="px-4 py-3">Last Checkpoint</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-800">
            {agents.map((agent) => {
              const statusKey = agent.status?.toUpperCase() ?? 'UNKNOWN';
              const statusClass =
                statusStyles[statusKey] ?? 'border-slate-600 bg-slate-800 text-slate-300';
              const tierClass = tierStyles[agent.tier] ?? 'border-slate-700 text-slate-200';

              return (
                <tr key={agent.agentId} className="bg-slate-950/40 transition hover:bg-slate-900/60">
                  <td className="px-4 py-3 font-mono text-xs text-slate-300">{agent.agentId}</td>
                  <td className="px-4 py-3">
                    <span
                      className={`rounded-full border px-3 py-1 text-xs font-semibold ${tierClass}`}
                    >
                      {agent.tier}
                    </span>
                  </td>
                  <td className="px-4 py-3">
                    <span className={`rounded-full border px-3 py-1 text-xs font-semibold ${statusClass}`}>
                      {statusKey}
                    </span>
                  </td>
                  <td className="px-4 py-3 font-mono text-xs text-slate-400">
                    {formatTimestamp(agent.lastCheckpoint ?? agent.lastHeartbeat)}
                  </td>
                </tr>
              );
            })}
            {agents.length === 0 && (
              <tr className="bg-slate-950/40">
                <td className="px-4 py-6 text-center text-sm text-slate-500" colSpan={4}>
                  No fleet data yet. Awaiting telemetry stream.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {message && (
        <div
          className={`mt-3 text-sm ${
            chaosStatus === 'success'
              ? 'text-emerald-400'
              : chaosStatus === 'error'
              ? 'text-red-400'
              : 'text-slate-400'
          }`}
        >
          {message}
        </div>
      )}
    </div>
  );
}
