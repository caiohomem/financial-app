import type { ReactNode } from "react";

type KPICardProps = {
  label: string;
  value: ReactNode;
  trend?: {
    direction: "up" | "down";
    percentage: number;
  };
  icon?: ReactNode;
};

export function KPICard({ label, value, trend, icon }: KPICardProps) {
  return (
    <div
      className="rounded-xl p-6 border transition-all duration-300 hover:shadow-lg"
      style={{
        backgroundColor: "var(--bg-secondary)",
        borderColor: "var(--border)",
      }}
      onMouseEnter={(e) => {
        e.currentTarget.style.backgroundColor = "var(--bg-tertiary)";
        e.currentTarget.style.borderColor = "var(--accent)";
      }}
      onMouseLeave={(e) => {
        e.currentTarget.style.backgroundColor = "var(--bg-secondary)";
        e.currentTarget.style.borderColor = "var(--border)";
      }}
    >
      <div className="flex items-start justify-between mb-4">
        <div>
          <p style={{ color: "var(--text-tertiary)", fontSize: "13px", fontWeight: 500 }} className="mb-1">
            {label}
          </p>
          <div style={{ color: "var(--text-primary)", fontSize: "28px", fontWeight: 700 }} className="font-bold">
            {value}
          </div>
        </div>
        {icon && <div style={{ color: "var(--accent)", fontSize: "20px" }}>{icon}</div>}
      </div>

      {trend && (
        <div
          className="flex items-center gap-1 text-sm font-medium"
          style={{
            color: trend.direction === "up" ? "var(--success)" : "var(--text-secondary)",
          }}
        >
          <span>{trend.direction === "up" ? "↑" : "↓"}</span>
          <span>{trend.percentage}% vs. mês anterior</span>
        </div>
      )}
    </div>
  );
}
