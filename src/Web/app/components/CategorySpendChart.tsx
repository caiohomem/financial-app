"use client";

import { PieChart, Pie, Cell, ResponsiveContainer, Tooltip, type PieLabelRenderProps } from "recharts";

type CatChartProps = {
  categories: Array<{ category: string; total: number }>;
};

const COLORS = [
  "#3B82F6", // blue
  "#60A5FA", // light blue
  "#22C55E", // green
  "#F59E0B", // amber
  "#EF4444", // red
  "#8B5CF6", // purple
  "#EC4899", // pink
  "#14B8A6", // teal
];

const renderLabel = (props: PieLabelRenderProps) => {
  const value = (props.value ?? 0) as number;
  return value.toLocaleString("pt-PT");
};

export function CategorySpendChart({ categories }: CatChartProps) {
  const data = categories.slice(0, 6).map((cat) => ({
    name: cat.category,
    value: Math.round(cat.total),
  }));

  if (data.length === 0) {
    return (
      <div
        className="rounded-xl p-8 border flex flex-col items-center justify-center h-80"
        style={{
          backgroundColor: "var(--bg-secondary)",
          borderColor: "var(--border)",
        }}
      >
        <p style={{ color: "var(--text-tertiary)" }}>Sem dados para este período</p>
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
        Gastos por Categoria
      </h3>
      <ResponsiveContainer width="100%" height={300}>
        <PieChart>
          <Pie
            data={data}
            cx="50%"
            cy="50%"
            innerRadius={80}
            outerRadius={120}
            paddingAngle={2}
            dataKey="value"
            label={renderLabel}
            labelLine={false}
          >
            {data.map((_, index) => (
              <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
            ))}
          </Pie>
          <Tooltip
            contentStyle={{
              backgroundColor: "var(--bg-tertiary)",
              border: "1px solid var(--border)",
              borderRadius: "8px",
              color: "var(--text-primary)",
            }}
            formatter={(value) => typeof value === 'number' ? value.toLocaleString("pt-PT") : value}
          />
        </PieChart>
      </ResponsiveContainer>
      <div className="mt-4 grid grid-cols-2 gap-2 text-xs">
        {data.map((item, idx) => (
          <div key={item.name} className="flex items-center gap-2">
            <div
              className="w-2 h-2 rounded-full"
              style={{ backgroundColor: COLORS[idx % COLORS.length] }}
            />
            <span style={{ color: "var(--text-secondary)" }}>
              {item.name} ({item.value.toLocaleString("pt-PT")})
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}
