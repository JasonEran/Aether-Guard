'use client';

import { useEffect, useState } from 'react';
import { fetchLatestTelemetry, TelemetryData } from '../lib/api';

export default function Page() {
  const [data, setData] = useState<TelemetryData | null>(null);

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

  const cpuColor = data.cpuUsage > 80 ? 'text-red-400' : 'text-emerald-400';
  const lastUpdated = new Date(data.timestamp * 1000).toLocaleTimeString();

  return (
    <main className="min-h-screen bg-slate-950 text-slate-100 p-8">
      <header className="max-w-5xl mx-auto">
        <h1 className="text-3xl font-semibold">Aether-Guard Live Monitor</h1>
        <div className="mt-4 inline-flex items-center gap-2 rounded-full border border-slate-800 bg-slate-900 px-4 py-2 text-sm text-slate-300">
          <span className="h-2 w-2 rounded-full bg-emerald-400"></span>
          Agent ID: {data.agentId}
        </div>
      </header>

      <section className="max-w-5xl mx-auto mt-8 grid gap-6 md:grid-cols-2">
        <div className="rounded-2xl border border-slate-800 bg-slate-900 p-6 shadow-lg">
          <div className="text-sm uppercase tracking-wide text-slate-400">CPU Usage</div>
          <div className={`mt-4 text-4xl font-bold ${cpuColor}`}>
            {data.cpuUsage.toFixed(1)}%
          </div>
          <div className="mt-2 text-xs text-slate-500">Threshold 80%</div>
        </div>

        <div className="rounded-2xl border border-slate-800 bg-slate-900 p-6 shadow-lg">
          <div className="text-sm uppercase tracking-wide text-slate-400">Memory Usage</div>
          <div className="mt-4 text-4xl font-bold text-blue-300">
            {data.memoryUsage.toFixed(1)}%
          </div>
          <div className="mt-2 text-xs text-slate-500">Available in real time</div>
        </div>

        <div className="md:col-span-2 rounded-2xl border border-slate-800 bg-slate-900 p-6">
          <div className="text-sm uppercase tracking-wide text-slate-400">Last Updated</div>
          <div className="mt-3 text-2xl font-semibold">{lastUpdated}</div>
        </div>
      </section>
    </main>
  );
}
