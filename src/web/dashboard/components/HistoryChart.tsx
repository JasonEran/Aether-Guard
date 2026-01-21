'use client';

import {
  CartesianGrid,
  Legend,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';

import type { TelemetryRecord } from '../lib/api';

interface HistoryChartProps {
  data: TelemetryRecord[];
}

interface TooltipProps {
  active?: boolean;
  payload?: Array<{ dataKey?: string; value?: number }>;
  label?: string | number;
}

const formatTime = (value: string | number) => {
  const date = typeof value === 'number' ? new Date(value) : new Date(value);
  return date.toLocaleTimeString();
};

const formatPercent = (value?: number) =>
  typeof value === 'number' && Number.isFinite(value) ? `${value.toFixed(1)}%` : '--';

function CustomTooltip({ active, payload, label }: TooltipProps) {
  if (!active || !payload?.length) {
    return null;
  }

  const cpu = payload.find((item) => item.dataKey === 'cpuUsage')?.value;
  const memory = payload.find((item) => item.dataKey === 'memoryUsage')?.value;
  const predicted = payload.find((item) => item.dataKey === 'predictedCpu')?.value;

  return (
    <div className="rounded-2xl border border-slate-800 bg-slate-900 px-4 py-3 text-sm text-slate-100 shadow-lg">
      <div className="font-semibold">Time: {formatTime(label ?? '')}</div>
      <div className="mt-2 text-emerald-400">CPU: {formatPercent(cpu)}</div>
      <div className="mt-1 text-blue-400">Memory: {formatPercent(memory)}</div>
      {typeof predicted === 'number' && Number.isFinite(predicted) && (
        <div className="mt-1 text-violet-400">Predicted CPU: {formatPercent(predicted)}</div>
      )}
    </div>
  );
}

export default function HistoryChart({ data }: HistoryChartProps) {
  return (
    <div className="h-[300px] w-full">
      <ResponsiveContainer width="100%" height="100%">
        <LineChart data={data} margin={{ top: 8, right: 16, left: 0, bottom: 8 }}>
          <CartesianGrid stroke="#1f2937" strokeDasharray="3 3" />
          <XAxis
            dataKey="timestamp"
            tickFormatter={formatTime}
            stroke="#94a3b8"
            tick={{ fill: '#94a3b8', fontSize: 12 }}
          />
          <YAxis
            domain={[0, 100]}
            stroke="#94a3b8"
            tick={{ fill: '#94a3b8', fontSize: 12 }}
          />
          <Tooltip content={<CustomTooltip />} />
          <Legend
            verticalAlign="top"
            height={24}
            iconType="circle"
            wrapperStyle={{ color: '#cbd5f5', fontSize: 12 }}
          />
          <Line
            type="monotone"
            dataKey="cpuUsage"
            name="Actual CPU"
            stroke="#10b981"
            strokeWidth={2}
            dot={false}
          />
          <Line
            type="monotone"
            dataKey="predictedCpu"
            name="Predicted CPU"
            stroke="#8b5cf6"
            strokeWidth={2}
            strokeDasharray="5 5"
            dot={false}
            connectNulls
          />
          <Line
            type="monotone"
            dataKey="memoryUsage"
            name="Memory"
            stroke="#3b82f6"
            strokeWidth={2}
            dot={false}
          />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}
