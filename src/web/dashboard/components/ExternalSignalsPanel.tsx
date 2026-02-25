'use client';

import type { ExternalSignal, ExternalSignalFeedState } from '../types';

interface ExternalSignalsPanelProps {
  signals: ExternalSignal[];
  feeds: ExternalSignalFeedState[];
  usingMock?: boolean;
}

const formatTimestamp = (value?: string) => {
  if (!value) {
    return '--';
  }

  const parsed = Date.parse(value);
  if (Number.isNaN(parsed)) {
    return value;
  }
  return new Date(parsed).toLocaleString();
};

const renderSeverity = (value?: string | null) => {
  if (!value) {
    return { label: 'Unknown', style: 'border-slate-700 bg-slate-900/60 text-slate-300' };
  }

  const normalized = value.toLowerCase();
  if (normalized.includes('critical') || normalized.includes('outage')) {
    return { label: value, style: 'border-red-500/50 bg-red-500/10 text-red-200' };
  }

  if (normalized.includes('warning') || normalized.includes('degraded')) {
    return { label: value, style: 'border-amber-500/50 bg-amber-500/10 text-amber-200' };
  }

  return { label: value, style: 'border-emerald-500/40 bg-emerald-500/10 text-emerald-200' };
};

const renderFeedStatus = (feed: ExternalSignalFeedState) => {
  if (feed.failureCount > 0) {
    return { label: 'Degraded', style: 'border-amber-500/50 bg-amber-500/10 text-amber-200' };
  }

  if (!feed.lastSuccessAt) {
    return { label: 'Pending', style: 'border-slate-700 bg-slate-900/60 text-slate-300' };
  }

  return { label: 'Healthy', style: 'border-emerald-500/40 bg-emerald-500/10 text-emerald-200' };
};

export default function ExternalSignalsPanel({ signals, feeds, usingMock }: ExternalSignalsPanelProps) {
  return (
    <div className="rounded-2xl border border-slate-800 bg-slate-900/70 p-6 shadow-[0_0_40px_rgba(15,23,42,0.6)]">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="text-xs uppercase tracking-[0.2em] text-slate-400">External Signals</div>
        <span
          className={`rounded-full border px-3 py-1 text-[10px] uppercase tracking-[0.2em] ${
            usingMock
              ? 'border-amber-500/40 bg-amber-500/10 text-amber-200'
              : 'border-emerald-500/40 bg-emerald-500/10 text-emerald-200'
          }`}
        >
          {usingMock ? 'Simulation' : 'Live Feeds'}
        </span>
      </div>

      <div className="mt-4 grid gap-4 lg:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)]">
        <div className="space-y-3">
          {signals.slice(0, 6).map((signal) => {
            const severity = renderSeverity(signal.severity);
            return (
              <div
                key={`${signal.source}-${signal.externalId}-${signal.id}`}
                className="rounded-xl border border-slate-800 bg-slate-950/50 px-4 py-3"
              >
                <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-slate-400">
                  <span className="uppercase tracking-[0.15em]">{signal.source}</span>
                  <span className={`rounded-full border px-2 py-0.5 text-[10px] ${severity.style}`}>
                    {severity.label}
                  </span>
                </div>
                <div className="mt-2 text-sm font-semibold text-slate-100">{signal.title}</div>
                {signal.summary && (
                  <div className="mt-1 text-xs text-slate-400 line-clamp-2">{signal.summary}</div>
                )}
                <div className="mt-2 flex flex-wrap items-center justify-between gap-2 text-xs text-slate-500">
                  <span>{signal.region ? `Region: ${signal.region}` : 'Global'}</span>
                  <span>{formatTimestamp(signal.publishedAt)}</span>
                </div>
              </div>
            );
          })}
          {signals.length === 0 && (
            <div className="rounded-xl border border-slate-800 bg-slate-950/40 px-4 py-6 text-center text-sm text-slate-500">
              No external signals ingested yet.
            </div>
          )}
        </div>

        <div className="space-y-3">
          <div className="text-xs uppercase tracking-[0.2em] text-slate-500">Feed Health</div>
          {feeds.map((feed) => {
            const status = renderFeedStatus(feed);
            return (
              <div
                key={feed.name}
                className="rounded-xl border border-slate-800 bg-slate-950/40 px-4 py-3 text-xs text-slate-200"
              >
                <div className="flex items-center justify-between gap-2">
                  <span className="font-semibold">{feed.name}</span>
                  <span className={`rounded-full border px-2 py-0.5 text-[10px] ${status.style}`}>
                    {status.label}
                  </span>
                </div>
                <div className="mt-2 text-[11px] text-slate-400">Last fetch: {formatTimestamp(feed.lastFetchAt)}</div>
                <div className="text-[11px] text-slate-500">
                  Last success: {formatTimestamp(feed.lastSuccessAt ?? undefined)}
                </div>
                {feed.failureCount > 0 && (
                  <div className="mt-1 text-[11px] text-amber-200">Failures: {feed.failureCount}</div>
                )}
              </div>
            );
          })}
          {feeds.length === 0 && (
            <div className="rounded-xl border border-slate-800 bg-slate-950/40 px-4 py-6 text-center text-sm text-slate-500">
              Feed status will appear after the first sync.
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
