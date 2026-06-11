"use client";

import { AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from "recharts";

type TrendChartProps = {
  data: Array<{ month: string; credits: number; debits: number }>;
};

export function MonthlyTrendChart({ data }: TrendChartProps) {
  if (data.length === 0) {
    return (
      <div
        className="rounded-xl p-8 border flex flex-col items-center justify-center h-80"
        style={{
          backgroundColor: "var(--bg-secondary)",
          borderColor: "var(--border)",
        }}
      >
        <p style={{ color: "var(--text-tertiary)" }}>Sem dados para exibir</p>
      </div>
    );
  }

  return (
    <div
      className="rounded-xl p-6 border"
      style={{
        backgroundColor: "var(--bg-secondary)",
        borderColor: "var(--border)",
      }}
    >
      <h3 style={{ color: "var(--text-primary)", marginTop: 0 }} className="text-sm font-semibold mb-4">
        Evolução Mensal
      </h3>
      <ResponsiveContainer width="100%" height={300}>
        <AreaChart data={data}>
          <defs>
            <linearGradient id="colorCredits" x1="0" y1="0" x2="0" y2="1">
              <stop offset="5%" stopColor="#22C55E" stopOpacity={0.3} />
              <stop offset="95%" stopColor="#22C55E" stopOpacity={0} />
            </linearGradient>
            <linearGradient id="colorDebits" x1="0" y1="0" x2="0" y2="1">
              <stop offset="5%" stopColor="#EF4444" stopOpacity={0.3} />
              <stop offset="95%" stopColor="#EF4444" stopOpacity={0} />
            </linearGradient>
          </defs>
          <CartesianGrid
            strokeDasharray="3 3"
            stroke="var(--border)"
            vertical={false}
          />
          <XAxis
            dataKey="month"
            stroke="var(--text-tertiary)"
            style={{ fontSize: "12px" }}
          />
          <YAxis
            stroke="var(--text-tertiary)"
            style={{ fontSize: "12px" }}
          />
          <Tooltip
            contentStyle={{
              backgroundColor: "var(--bg-tertiary)",
              border: "1px solid var(--border)",
              borderRadius: "8px",
              color: "var(--text-primary)",
            }}
            cursor={{ fill: "rgba(59, 130, 246, 0.1)" }}
            formatter={(value) => typeof value === 'number' ? value.toLocaleString("pt-PT") : value}
          />
          <Legend
            wrapperStyle={{ color: "var(--text-secondary)", fontSize: "12px" }}
          />
          <Area
            type="monotone"
            dataKey="credits"
            stroke="#22C55E"
            strokeWidth={2}
            fill="url(#colorCredits)"
            name="Créditos"
          />
          <Area
            type="monotone"
            dataKey="debits"
            stroke="#EF4444"
            strokeWidth={2}
            fill="url(#colorDebits)"
            name="Débitos"
          />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}
