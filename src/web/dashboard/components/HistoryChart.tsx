'use client';

import {
  CartesianGrid,
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

const formatTime = (value: string | number) => {
  const date = typeof value === 'number' ? new Date(value) : new Date(value);
  return date.toLocaleTimeString();
};

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
          <Tooltip
            contentStyle={{
              backgroundColor: '#0f172a',
              border: '1px solid #1f2937',
              borderRadius: 12,
              color: '#e2e8f0',
            }}
            labelFormatter={(value) => `Time: ${formatTime(value as string | number)}`}
            formatter={(value: number, name: string) => [`${value.toFixed(1)}%`, name]}
          />
          <Line
            type="monotone"
            dataKey="cpuUsage"
            name="CPU"
            stroke="#10b981"
            strokeWidth={2}
            dot={false}
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
