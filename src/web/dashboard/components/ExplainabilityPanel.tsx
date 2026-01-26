'use client';

import type { Agent } from '../types';

interface ExplainabilityPanelProps {
  agent?: Agent;
  usingMock?: boolean;
}

const statusStyles: Record<string, string> = {
  CRITICAL: 'border-red-500/40 bg-red-500/10 text-red-200',
  HIGH: 'border-amber-500/40 bg-amber-500/10 text-amber-200',
  MEDIUM: 'border-amber-500/40 bg-amber-500/10 text-amber-200',
  LOW: 'border-emerald-500/40 bg-emerald-500/10 text-emerald-200',
  UNAVAILABLE: 'border-slate-600 bg-slate-800 text-slate-300',
  PENDING: 'border-slate-600 bg-slate-800 text-slate-300',
};

const formatConfidence = (value?: number) => {
  if (value === undefined || Number.isNaN(value)) {
    return '--';
  }
  const normalized = value > 1 ? value / 100 : value;
  return `${(normalized * 100).toFixed(1)}%`;
};

const formatPrediction = (value?: number) => {
  if (value === undefined || Number.isNaN(value)) {
    return '--';
  }
  return `${Math.round(value)}%`;
};

const formatDiskAvailable = (value?: number) => {
  if (value === undefined || Number.isNaN(value)) {
    return '--';
  }
  const gb = value / (1024 * 1024 * 1024);
  return `${gb.toFixed(1)} GB`;
};

const formatRootCause = (value?: string) => {
  const normalized = value?.trim();
  if (!normalized || normalized.toLowerCase() === 'unavailable') {
    return 'Awaiting AI analysis or failure details.';
  }
  return normalized;
};

export default function ExplainabilityPanel({ agent, usingMock }: ExplainabilityPanelProps) {
  const statusLabel = agent?.analysisStatus?.trim().toUpperCase() || 'PENDING';
  const statusClass = statusStyles[statusLabel] ?? statusStyles.PENDING;

  return (
    <div className="rounded-2xl border border-slate-800 bg-slate-900/70 p-6 shadow-[0_0_40px_rgba(15,23,42,0.6)]">
      <div className="flex items-center justify-between gap-3">
        <div className="text-xs uppercase tracking-[0.2em] text-slate-400">Explainability</div>
        {usingMock && (
          <span className="rounded-full border border-amber-500/40 bg-amber-500/10 px-3 py-1 text-xs uppercase tracking-[0.2em] text-amber-200">
            Simulated
          </span>
        )}
      </div>

      {!agent ? (
        <div className="mt-4 text-sm text-slate-400">Awaiting telemetry and AI analysis.</div>
      ) : (
        <>
          <div className="mt-4 grid gap-4 sm:grid-cols-2">
            <div className="space-y-3">
              <div className="text-xs uppercase tracking-[0.2em] text-slate-500">AI Assessment</div>
              <div className="flex flex-wrap items-center gap-3 text-sm text-slate-200">
                <span className={`rounded-full border px-3 py-1 text-xs font-semibold ${statusClass}`}>
                  {statusLabel}
                </span>
                <span className="text-slate-400">Confidence: {formatConfidence(agent.analysisConfidence)}</span>
                <span className="text-slate-400">Predicted CPU: {formatPrediction(agent.predictedCpu)}</span>
              </div>
            </div>
            <div className="space-y-3">
              <div className="text-xs uppercase tracking-[0.2em] text-slate-500">Telemetry Context</div>
              <div className="text-sm text-slate-200">
                Rebalance Signal:{' '}
                <span className={agent.rebalanceSignal ? 'text-amber-200' : 'text-emerald-200'}>
                  {agent.rebalanceSignal ? 'Active' : 'Stable'}
                </span>
              </div>
              <div className="text-sm text-slate-200">
                Disk Available: <span className="text-slate-300">{formatDiskAvailable(agent.diskAvailable)}</span>
              </div>
            </div>
          </div>

          <div className="mt-4 rounded-xl border border-slate-800 bg-slate-950/40 p-4">
            <div className="text-xs uppercase tracking-[0.2em] text-slate-500">Root Cause</div>
            <div className="mt-2 text-sm text-slate-200">{formatRootCause(agent.rootCause)}</div>
          </div>
        </>
      )}
    </div>
  );
}
