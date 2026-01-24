'use client';

import {
  Area,
  AreaChart,
  CartesianGrid,
  Legend,
  ReferenceArea,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';

import type { RiskPoint } from '../lib/api';

interface HistoryChartProps {
  data: RiskPoint[];
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

const formatRisk = (value?: number) =>
  typeof value === 'number' && Number.isFinite(value) ? value.toFixed(2) : '--';

function CustomTooltip({ active, payload, label }: TooltipProps) {
  if (!active || !payload?.length) {
    return null;
  }

  const risk = payload.find((item) => item.dataKey === 'riskScore')?.value;
  const riskValue = typeof risk === 'number' ? risk : undefined;
  const status = typeof riskValue === 'number' && riskValue > 0.8 ? 'CRITICAL' : 'SAFE';

  return (
    <div className="rounded-2xl border border-slate-800 bg-slate-900 px-4 py-3 text-sm text-slate-100 shadow-lg">
      <div className="font-semibold">Time: {formatTime(label ?? '')}</div>
      <div className="mt-2 text-amber-300">Risk Score: {formatRisk(riskValue)}</div>
      <div className={status === 'CRITICAL' ? 'mt-1 text-red-400' : 'mt-1 text-emerald-400'}>
        Status: {status}
      </div>
    </div>
  );
}

export default function HistoryChart({ data }: HistoryChartProps) {
  return (
    <div className="h-[300px] w-full">
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart data={data} margin={{ top: 8, right: 16, left: 0, bottom: 8 }}>
          <defs>
            <linearGradient id="riskGlow" x1="0" x2="0" y1="0" y2="1">
              <stop offset="0%" stopColor="#f97316" stopOpacity={0.45} />
              <stop offset="100%" stopColor="#f97316" stopOpacity={0.05} />
            </linearGradient>
          </defs>
          <CartesianGrid stroke="#1f2937" strokeDasharray="3 3" />
          <ReferenceArea y1={0} y2={0.8} fill="#064e3b" fillOpacity={0.25} />
          <ReferenceArea y1={0.8} y2={1} fill="#7f1d1d" fillOpacity={0.25} />
          <ReferenceLine
            y={0.8}
            stroke="#ef4444"
            strokeDasharray="4 4"
            label={{ value: 'Critical 0.8', position: 'insideTopRight', fill: '#fca5a5', fontSize: 11 }}
          />
          <XAxis
            dataKey="timestamp"
            tickFormatter={formatTime}
            stroke="#94a3b8"
            tick={{ fill: '#94a3b8', fontSize: 12 }}
          />
          <YAxis
            domain={[0, 1]}
            stroke="#94a3b8"
            tick={{ fill: '#94a3b8', fontSize: 12 }}
            tickFormatter={(value) => value.toFixed(1)}
          />
          <Tooltip content={<CustomTooltip />} />
          <Legend
            verticalAlign="top"
            height={24}
            iconType="circle"
            wrapperStyle={{ color: '#cbd5f5', fontSize: 12 }}
          />
          <Area
            type="monotone"
            dataKey="riskScore"
            name="Risk Score"
            stroke="#f97316"
            strokeWidth={2}
            fill="url(#riskGlow)"
            dot={false}
          />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}
