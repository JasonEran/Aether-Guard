'use client';

import { useState } from 'react';

import type { Agent } from '../types';

type FleetEntry = Agent & { lastCheckpoint?: string };

interface ControlPanelProps {
  agents: FleetEntry[];
  onSimulateChaos?: () => Promise<void>;
  userRole?: string;
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

export default function ControlPanel({ agents, onSimulateChaos, userRole }: ControlPanelProps) {
  const [chaosStatus, setChaosStatus] = useState<'idle' | 'loading' | 'success' | 'error'>('idle');
  const [message, setMessage] = useState<string>('');
  const [diagnosticsStatus, setDiagnosticsStatus] = useState<'idle' | 'loading' | 'success' | 'error'>('idle');
  const [diagnosticsMessage, setDiagnosticsMessage] = useState<string>('');
  const [includeSnapshots, setIncludeSnapshots] = useState(false);
  const isAdmin = (userRole ?? 'VIEWER') === 'ADMIN';

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

  const handleDiagnostics = async () => {
    if (!isAdmin) {
      setDiagnosticsStatus('error');
      setDiagnosticsMessage('Admin role required to export diagnostics.');
      setTimeout(() => {
        setDiagnosticsStatus('idle');
        setDiagnosticsMessage('');
      }, 4000);
      return;
    }

    setDiagnosticsStatus('loading');
    setDiagnosticsMessage('Preparing diagnostics bundle...');

    try {
      const response = await fetch(
        `/api/diagnostics/bundle?includeSnapshots=${includeSnapshots.toString()}`,
        { cache: 'no-store' },
      );
      if (!response.ok) {
        const text = await response.text();
        throw new Error(text || 'Diagnostics bundle export failed.');
      }

      const contentDisposition = response.headers.get('content-disposition') ?? '';
      const filenameMatch = contentDisposition.match(/filename="?([^";]+)"?/i);
      const filename = filenameMatch?.[1] ?? 'aetherguard-diagnostics.zip';

      const blob = await response.blob();
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = filename;
      document.body.appendChild(link);
      link.click();
      link.remove();
      window.URL.revokeObjectURL(url);

      setDiagnosticsStatus('success');
      setDiagnosticsMessage('Diagnostics bundle downloaded.');
    } catch (error) {
      setDiagnosticsStatus('error');
      setDiagnosticsMessage(error instanceof Error ? error.message : 'Diagnostics export failed.');
    }

    setTimeout(() => {
      setDiagnosticsStatus('idle');
      setDiagnosticsMessage('');
    }, 4000);
  };

  return (
    <div className="rounded-2xl border border-slate-800 bg-slate-900/70 p-6 shadow-[0_0_40px_rgba(15,23,42,0.6)]">
      <div className="flex items-center justify-between gap-4">
        <div>
          <div className="text-xs uppercase tracking-[0.2em] text-slate-400">Fleet Status</div>
          <div className="mt-2 text-lg font-semibold text-slate-100">Migration Control Grid</div>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <button
            type="button"
            onClick={handleChaos}
            disabled={chaosStatus === 'loading'}
            className="whitespace-nowrap rounded-lg border border-amber-500/60 bg-amber-500/20 px-4 py-2 text-xs font-semibold uppercase tracking-[0.2em] text-amber-200 transition hover:border-amber-400 hover:bg-amber-500/30 disabled:cursor-not-allowed disabled:border-slate-700 disabled:bg-slate-800"
          >
            {chaosStatus === 'loading' ? 'Signaling...' : 'Simulate Chaos'}
          </button>
          <button
            type="button"
            onClick={handleDiagnostics}
            disabled={diagnosticsStatus === 'loading'}
            className="whitespace-nowrap rounded-lg border border-slate-600 bg-slate-800/70 px-4 py-2 text-xs font-semibold uppercase tracking-[0.2em] text-slate-200 transition hover:border-slate-400 hover:text-slate-100 disabled:cursor-not-allowed disabled:border-slate-700 disabled:text-slate-500"
          >
            {diagnosticsStatus === 'loading' ? 'Exporting...' : 'Export Diagnostics'}
          </button>
        </div>
      </div>

      <div className="mt-4 flex flex-wrap items-center gap-3 text-xs text-slate-400">
        <label className="flex items-center gap-2">
          <input
            type="checkbox"
            className="h-3 w-3 rounded border-slate-600 bg-slate-900 text-emerald-400"
            checked={includeSnapshots}
            onChange={(event) => setIncludeSnapshots(event.target.checked)}
          />
          Include snapshots (may increase bundle size).
        </label>
        {!isAdmin && (
          <span className="rounded-full border border-slate-700 bg-slate-900/60 px-2 py-1 text-[10px] uppercase tracking-[0.2em] text-slate-400">
            Admin required
          </span>
        )}
      </div>

      <div className="mt-5 overflow-x-auto rounded-xl border border-slate-800">
        <table className="min-w-[520px] w-full table-fixed text-left text-sm text-slate-200">
          <thead className="bg-slate-900/80 text-xs uppercase tracking-[0.2em] text-slate-400">
            <tr>
              <th className="px-4 py-3 whitespace-nowrap">Agent ID</th>
              <th className="px-4 py-3 whitespace-nowrap">Workload Tier</th>
              <th className="px-4 py-3 whitespace-nowrap">Current State</th>
              <th className="px-4 py-3 whitespace-nowrap">Last Checkpoint</th>
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
                  <td className="px-4 py-3 font-mono text-xs text-slate-300">
                    <span className="block max-w-[140px] truncate" title={agent.agentId}>
                      {agent.agentId}
                    </span>
                  </td>
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
                  <td className="px-4 py-3 font-mono text-xs text-slate-400 whitespace-nowrap">
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
      {diagnosticsMessage && (
        <div
          className={`mt-3 text-sm ${
            diagnosticsStatus === 'success'
              ? 'text-emerald-400'
              : diagnosticsStatus === 'error'
              ? 'text-red-400'
              : 'text-slate-400'
          }`}
        >
          {diagnosticsMessage}
        </div>
      )}
    </div>
  );
}
