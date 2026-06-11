"use client";

import { useState } from "react";
import { getDateRangeMonths } from "../lib/format";

type Props = {
  onMonthsChange: (months: string[]) => void;
};

type Preset = "1m" | "3m" | "6m" | "1y" | "all" | "custom";

export function DateRangeSelector({ onMonthsChange }: Props) {
  const [preset, setPreset] = useState<Preset>("1m");
  const [customDays, setCustomDays] = useState(30);
  const [isOpen, setIsOpen] = useState(false);

  const handlePresetChange = (newPreset: Preset) => {
    setPreset(newPreset);
    if (newPreset === "custom") return;

    const months = getDateRangeMonths(null, newPreset === "1m" ? undefined : newPreset);
    onMonthsChange(months);
    setIsOpen(false);
  };

  const handleCustomDaysChange = (days: number) => {
    setCustomDays(days);
    const months = getDateRangeMonths(days);
    onMonthsChange(months);
    setIsOpen(false);
  };

  const getLabel = () => {
    if (preset === "1m") return "Último mês";
    if (preset === "3m") return "Últimos 3 meses";
    if (preset === "6m") return "Últimos 6 meses";
    if (preset === "1y") return "Último ano";
    if (preset === "all") return "Desde 2012";
    if (preset === "custom") return `Últimos ${customDays} dias`;
    return "Selecionar período";
  };

  return (
    <div className="relative">
      <button
        onClick={() => setIsOpen(!isOpen)}
        className="px-4 py-2 rounded-lg border text-sm font-medium transition-colors"
        style={{
          backgroundColor: "var(--bg-secondary)",
          borderColor: "var(--border)",
          color: "var(--text-primary)",
        }}
        aria-label="Selecionar período"
      >
        {getLabel()}
      </button>

      {isOpen && (
        <div
          className="absolute right-0 mt-2 w-48 rounded-lg border shadow-lg z-50"
          style={{
            backgroundColor: "var(--bg-secondary)",
            borderColor: "var(--border)",
          }}
        >
          <div className="p-3 border-b" style={{ borderColor: "var(--border)" }}>
            <button
              onClick={() => handlePresetChange("1m")}
              className={`w-full text-left px-3 py-2 rounded text-sm mb-2 transition-colors ${
                preset === "1m" ? "font-semibold" : ""
              }`}
              style={{
                backgroundColor: preset === "1m" ? "var(--accent)" : "transparent",
                color: preset === "1m" ? "white" : "var(--text-primary)",
              }}
            >
              Último mês
            </button>
            <button
              onClick={() => handlePresetChange("3m")}
              className={`w-full text-left px-3 py-2 rounded text-sm mb-2 transition-colors ${
                preset === "3m" ? "font-semibold" : ""
              }`}
              style={{
                backgroundColor: preset === "3m" ? "var(--accent)" : "transparent",
                color: preset === "3m" ? "white" : "var(--text-primary)",
              }}
            >
              Últimos 3 meses
            </button>
            <button
              onClick={() => handlePresetChange("6m")}
              className={`w-full text-left px-3 py-2 rounded text-sm mb-2 transition-colors ${
                preset === "6m" ? "font-semibold" : ""
              }`}
              style={{
                backgroundColor: preset === "6m" ? "var(--accent)" : "transparent",
                color: preset === "6m" ? "white" : "var(--text-primary)",
              }}
            >
              Últimos 6 meses
            </button>
            <button
              onClick={() => handlePresetChange("1y")}
              className={`w-full text-left px-3 py-2 rounded text-sm mb-2 transition-colors ${
                preset === "1y" ? "font-semibold" : ""
              }`}
              style={{
                backgroundColor: preset === "1y" ? "var(--accent)" : "transparent",
                color: preset === "1y" ? "white" : "var(--text-primary)",
              }}
            >
              Último ano
            </button>
            <button
              onClick={() => handlePresetChange("all")}
              className={`w-full text-left px-3 py-2 rounded text-sm transition-colors ${
                preset === "all" ? "font-semibold" : ""
              }`}
              style={{
                backgroundColor: preset === "all" ? "var(--accent)" : "transparent",
                color: preset === "all" ? "white" : "var(--text-primary)",
              }}
            >
              Desde 2012
            </button>
          </div>

          <div className="p-3">
            <label
              style={{ color: "var(--text-secondary)" }}
              className="block text-xs font-medium mb-2"
            >
              Custom (dias)
            </label>
            <div className="flex gap-2">
              <input
                type="number"
                min="1"
                max="3650"
                value={customDays}
                onChange={(e) => {
                  const days = Math.max(1, Math.min(3650, Number(e.target.value)));
                  setCustomDays(days);
                }}
                className="flex-1 px-2 py-1 rounded text-sm border"
                style={{
                  backgroundColor: "var(--bg-tertiary)",
                  borderColor: "var(--border)",
                  color: "var(--text-primary)",
                }}
              />
              <button
                onClick={() => {
                  setPreset("custom");
                  handleCustomDaysChange(customDays);
                }}
                className="px-3 py-1 rounded text-sm font-medium transition-colors"
                style={{
                  backgroundColor: "var(--accent)",
                  color: "white",
                }}
              >
                Ir
              </button>
            </div>
          </div>
        </div>
      )}

      {isOpen && (
        <div
          className="fixed inset-0"
          onClick={() => setIsOpen(false)}
          style={{ zIndex: 40 }}
        />
      )}
    </div>
  );
}
