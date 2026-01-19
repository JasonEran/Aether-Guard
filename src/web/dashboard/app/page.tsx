'use client';

import { useEffect, useState } from 'react';
import { fetchLatestTelemetry, DashboardData } from '../lib/api';

const ShieldIcon = ({ className }: { className?: string }) => (
  <svg
    className={className}
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.6"
    strokeLinecap="round"
    strokeLinejoin="round"
    aria-hidden="true"
  >
    <path d="M12 2l7 4v6c0 5-3.5 9-7 10-3.5-1-7-5-7-10V6l7-4z" />
    <path d="M9 12l2 2 4-4" />
  </svg>
);

const WarningIcon = ({ className }: { className?: string }) => (
  <svg
    className={className}
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.6"
    strokeLinecap="round"
    strokeLinejoin="round"
    aria-hidden="true"
  >
    <path d="M12 3l9 16H3l9-16z" />
    <path d="M12 9v4" />
    <path d="M12 17h.01" />
  </svg>
);

export default function Page() {
  const [data, setData] = useState<DashboardData | null>(null);

  useEffect(() => {
    let isMounted = true;

    const load = async () => {
      const latest = await fetchLatestTelemetry();
      if (isMounted) {
        setData(latest);
      }
    };

    load();
    const intervalId = setInterval(load, 1000);

    return () => {
      isMounted = false;
      clearInterval(intervalId);
    };
  }, []);

  if (!data) {
    return (
      <main className="min-h-screen bg-slate-950 text-slate-100 flex items-center justify-center">
        <div className="text-center">
          <div className="text-2xl font-semibold">Aether-Guard Live Monitor</div>
          <div className="mt-4 text-slate-400">Connecting to Core...</div>
        </div>
      </main>
    );
  }

  const cpuUsage = data.telemetry.cpuUsage;
  const memoryUsage = data.telemetry.memoryUsage;
  const cpuColor = cpuUsage > 80 ? 'text-red-400' : 'text-emerald-400';
  const lastUpdated = new Date(data.telemetry.timestamp * 1000).toLocaleTimeString();

  const analysisStatus = data.analysis?.status;
  const isHealthy = analysisStatus === 'Normal';
  const isCritical = analysisStatus === 'Critical';
  const aiLabel = isCritical ? 'Risk Detected' : isHealthy ? 'Healthy' : 'Unknown';
  const aiColor = isCritical ? 'text-red-400' : isHealthy ? 'text-emerald-400' : 'text-slate-400';
  const confidence = data.analysis?.confidence;
  const confidenceLabel =
    typeof confidence === 'number' ? `Confidence: ${(confidence * 100).toFixed(0)}%` : 'Confidence: --';

  return (
    <main className="min-h-screen bg-slate-950 text-slate-100 p-8">
      <header className="max-w-5xl mx-auto">
        <h1 className="text-3xl font-semibold">Aether-Guard Live Monitor</h1>
        <div className="mt-4 inline-flex items-center gap-2 rounded-full border border-slate-800 bg-slate-900 px-4 py-2 text-sm text-slate-300">
          <span className="h-2 w-2 rounded-full bg-emerald-400"></span>
          Agent ID: {data.telemetry.agentId}
        </div>
      </header>

      <section className="max-w-5xl mx-auto mt-8 grid gap-6 md:grid-cols-3">
        <div className="rounded-2xl border border-slate-800 bg-slate-900 p-6 shadow-lg">
          <div className="text-sm uppercase tracking-wide text-slate-400">CPU Usage</div>
          <div className={`mt-4 text-4xl font-bold ${cpuColor}`}>
            {cpuUsage.toFixed(1)}%
          </div>
          <div className="mt-2 text-xs text-slate-500">Threshold 80%</div>
        </div>

        <div className="rounded-2xl border border-slate-800 bg-slate-900 p-6 shadow-lg">
          <div className="text-sm uppercase tracking-wide text-slate-400">Memory Usage</div>
          <div className="mt-4 text-4xl font-bold text-blue-300">
            {memoryUsage.toFixed(1)}%
          </div>
          <div className="mt-2 text-xs text-slate-500">Available in real time</div>
        </div>

        <div className="rounded-2xl border border-slate-800 bg-slate-900 p-6 shadow-lg">
          <div className="text-sm uppercase tracking-wide text-slate-400">AI Health Status</div>
          <div className={`mt-4 flex items-center gap-2 text-3xl font-bold ${aiColor}`}>
            {isCritical ? <WarningIcon className="h-7 w-7" /> : <ShieldIcon className="h-7 w-7" />}
            {aiLabel}
          </div>
          <div className="mt-2 text-xs text-slate-500">{confidenceLabel}</div>
        </div>

        <div className="md:col-span-3 rounded-2xl border border-slate-800 bg-slate-900 p-6">
          <div className="text-sm uppercase tracking-wide text-slate-400">Last Updated</div>
          <div className="mt-3 text-2xl font-semibold">{lastUpdated}</div>
        </div>
      </section>
    </main>
  );
}
