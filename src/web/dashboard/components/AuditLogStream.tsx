'use client';

import type { AuditLog } from '../types';

interface AuditLogStreamProps {
  logs: AuditLog[];
}

const formatTimestamp = (value: string) => {
  const parsed = Date.parse(value);
  if (Number.isNaN(parsed)) {
    return value;
  }
  return new Date(parsed).toLocaleTimeString();
};

const highlightAction = (action: string, result: string, error?: string) => {
  const normalizedAction = action.toLowerCase();
  const normalizedResult = result.toLowerCase();
  const normalizedError = error?.toLowerCase() ?? '';

  if (normalizedAction === 'migration completed') {
    return 'border-emerald-500/40 bg-emerald-500/10 text-emerald-200';
  }

  if (
    normalizedAction.includes('failed') ||
    normalizedResult.includes('fail') ||
    normalizedResult.includes('error') ||
    normalizedError.includes('fail') ||
    normalizedError.includes('error')
  ) {
    return 'border-red-500/40 bg-red-500/10 text-red-200';
  }

  return 'border-slate-700 bg-slate-900/60 text-slate-200';
};

export default function AuditLogStream({ logs }: AuditLogStreamProps) {
  return (
    <div className="rounded-2xl border border-slate-800 bg-slate-900/70 p-6 shadow-[0_0_30px_rgba(15,23,42,0.5)]">
      <div className="text-xs uppercase tracking-[0.2em] text-slate-400">Audit Log Stream</div>
      <div className="mt-4 space-y-3 overflow-y-auto pr-2 text-sm text-slate-200" style={{ maxHeight: 280 }}>
        {logs.map((log) => (
          <div
            key={log.id}
            className={`rounded-xl border px-4 py-3 ${highlightAction(log.action, log.result, log.error)}`}
          >
            <div className="flex items-center justify-between text-xs uppercase tracking-[0.15em] text-slate-400">
              <span>{formatTimestamp(log.timestamp)}</span>
              <span className="font-mono text-[11px]">Agent {log.agentId}</span>
            </div>
            <div className="mt-2 text-sm font-semibold">{log.action}</div>
            <div className="mt-1 text-xs text-slate-300">{log.result}</div>
            {log.error && (
              <div className="mt-2 text-xs text-red-200">Error: {log.error}</div>
            )}
          </div>
        ))}
        {logs.length === 0 && (
          <div className="rounded-xl border border-slate-800 bg-slate-950/40 px-4 py-6 text-center text-sm text-slate-500">
            Awaiting migration activity...
          </div>
        )}
      </div>
    </div>
  );
}
