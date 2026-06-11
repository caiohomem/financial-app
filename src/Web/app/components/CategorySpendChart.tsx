"use client";

import { PieChart, Pie, Cell, ResponsiveContainer, Tooltip, type PieLabelRenderProps } from "recharts";

type CatChartProps = {
  categories: Array<{ category: string; total: number }>;
  selectedCategory?: string;
  onCategoryClick?: (category: string) => void;
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

export function CategorySpendChart({ categories, selectedCategory, onCategoryClick }: CatChartProps) {
  const total = categories.reduce((sum, cat) => sum + cat.total, 0);

  const pieData = categories.slice(0, 7).map((cat) => ({
    name: cat.category,
    value: Math.round(cat.total),
  }));

  if (pieData.length === 0) {
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
      <div className="flex items-center justify-between mb-4">
        <h3 style={{ color: "var(--text-primary)", marginTop: 0, marginBottom: 0 }} className="text-sm font-semibold">
          Gastos por Categoria
        </h3>
        {selectedCategory && (
          <button
            onClick={() => onCategoryClick?.(selectedCategory)}
            className="px-2 py-1 rounded text-xs font-medium"
            style={{ backgroundColor: "var(--bg-tertiary)", color: "var(--text-secondary)" }}
          >
            {selectedCategory} ✕
          </button>
        )}
      </div>
      <ResponsiveContainer width="100%" height={240}>
        <PieChart>
          <Pie
            data={pieData}
            cx="50%"
            cy="50%"
            innerRadius={62}
            outerRadius={95}
            paddingAngle={2}
            dataKey="value"
            label={renderLabel}
            labelLine={false}
            onClick={(entry) => {
              const name = (entry as { name?: string }).name;
              if (name) onCategoryClick?.(name);
            }}
            style={{ cursor: onCategoryClick ? "pointer" : "default" }}
          >
            {pieData.map((item, index) => (
              <Cell
                key={`cell-${index}`}
                fill={COLORS[index % COLORS.length]}
                fillOpacity={!selectedCategory || selectedCategory === item.name ? 1 : 0.3}
              />
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

      {/* Lista ranqueada: todas as categorias com valor e % do total, clicáveis */}
      <div className="mt-4 space-y-1 max-h-48 overflow-y-auto pr-1">
        {categories.map((item, idx) => {
          const share = total > 0 ? (item.total / total) * 100 : 0;
          const isSelected = selectedCategory === item.category;
          const color = COLORS[idx % COLORS.length];
          return (
            <button
              key={item.category}
              onClick={() => onCategoryClick?.(item.category)}
              className="w-full flex items-center gap-2 px-2 py-1.5 rounded text-xs transition-colors text-left"
              style={{
                backgroundColor: isSelected ? "var(--bg-tertiary)" : "transparent",
                outline: isSelected ? "1px solid var(--accent)" : "none",
                cursor: onCategoryClick ? "pointer" : "default",
              }}
              onMouseEnter={(e) => {
                if (!isSelected) e.currentTarget.style.backgroundColor = "var(--bg-tertiary)";
              }}
              onMouseLeave={(e) => {
                if (!isSelected) e.currentTarget.style.backgroundColor = "transparent";
              }}
            >
              <span
                className="w-2 h-2 rounded-full shrink-0"
                style={{ backgroundColor: idx < 7 ? color : "var(--text-tertiary)" }}
              />
              <span className="flex-1 truncate" style={{ color: "var(--text-secondary)" }}>
                {item.category}
              </span>
              <span className="font-medium tabular-nums" style={{ color: "var(--text-primary)" }}>
                {item.total.toLocaleString("pt-PT", { minimumFractionDigits: 0, maximumFractionDigits: 0 })}
              </span>
              <span className="w-12 text-right tabular-nums" style={{ color: "var(--text-tertiary)" }}>
                {share.toFixed(1)}%
              </span>
            </button>
          );
        })}
      </div>
    </div>
  );
}
