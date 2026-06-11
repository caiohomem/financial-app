"use client";

import { useEffect, useState } from "react";
import styles from "./report.module.css";
import { getMonthlyReport, type MonthlyAnomaly, type MonthlyReport } from "../lib/api";

const REPORT_FALLBACK = "Relatorio indisponivel - chave de API nao configurada.";

export default function ReportPage() {
  const [selectedMonth, setSelectedMonth] = useState(() => monthKey(new Date()));
  const [report, setReport] = useState<MonthlyReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function loadReport() {
      try {
        setLoading(true);
        setError(null);

        const response = await getMonthlyReport(selectedMonth);

        if (!cancelled) {
          setReport(response);
        }
      } catch (loadError) {
        if (!cancelled) {
          setError(loadError instanceof Error ? loadError.message : "Failed to load report");
          setReport(null);
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    void loadReport();

    return () => {
      cancelled = true;
    };
  }, [selectedMonth]);

  if (loading) {
    return (
      <div style={{ backgroundColor: "var(--bg-primary)" }} className="min-h-screen flex flex-col items-center justify-center px-4">
        <div className="rounded-xl p-8 border text-center" style={{ backgroundColor: "var(--bg-secondary)", borderColor: "var(--border)" }}>
          <div className="mb-4 inline-block">
            <div className="w-12 h-12 rounded-lg animate-spin" style={{ borderTop: "3px solid var(--accent)", borderRight: "3px solid transparent" }} />
          </div>
          <h1 style={{ color: "var(--text-primary)", marginTop: 0 }} className="text-lg font-semibold">
            A gerar relatório mensal
          </h1>
          <p style={{ color: "var(--text-tertiary)" }} className="text-sm">
            A gerar relatório e a analisar anomalias do mês selecionado.
          </p>
        </div>
      </div>
    );
  }

  if (error || !report) {
    return (
      <div style={{ backgroundColor: "var(--bg-primary)" }} className="min-h-screen flex flex-col items-center justify-center px-4">
        <div className="rounded-xl p-8 border text-center max-w-md" style={{ backgroundColor: "var(--bg-secondary)", borderColor: "var(--border)" }}>
          <h1 style={{ color: "var(--error)", marginTop: 0 }} className="text-lg font-semibold">
            Falha ao carregar o relatório
          </h1>
          <p style={{ color: "var(--text-secondary)" }} className="text-sm mb-6">
            {error ?? "Sem dados para apresentar."}
          </p>
          <button
            onClick={() => window.location.reload()}
            className="px-6 py-2 rounded-lg font-medium transition-all"
            style={{ backgroundColor: "var(--accent)", color: "white" }}
          >
            Tentar novamente
          </button>
        </div>
      </div>
    );
  }

  const previousDelta = getPreviousMonthDelta(
    report.aggregations.totalOut,
    report.aggregations.priorMonthTotalOut,
  );

  return (
    <div style={{ backgroundColor: "var(--bg-primary)" }} className="min-h-screen">
      <div className="max-w-7xl mx-auto px-6 py-8">
        <header className={styles.hero}>
          <p className={styles.eyebrow}>Financial App · relatorio mensal</p>
          <div className={styles.titleRow}>
            <div>
              <h1 className={styles.title}>Relatório mensal</h1>
              <p className={styles.subtitle}>
                Leitura mensal em linguagem natural, comparacao com o periodo anterior e apoio
                visual para categorias, merchants e desvios relevantes.
              </p>
            </div>
            <select
              className={styles.monthSelect}
              value={selectedMonth}
              onChange={(event) => setSelectedMonth(event.target.value)}
              aria-label="Selecionar mes do relatorio"
            >
              {last12Months().map((month) => (
                <option key={month} value={month}>
                  {formatMonthLabel(month)}
                </option>
              ))}
            </select>
          </div>
        </header>

        <section className={styles.panel}>
          <div className={styles.panelBody}>
            <div className={styles.sectionHeader}>
              <div>
                <h2 className={styles.sectionTitle}>Narrativa do mes</h2>
                <p className={styles.sectionDescription}>
                  Texto gerado a partir das agregacoes e das anomalias detetadas.
                </p>
              </div>
            </div>
            <p className={styles.reportText}>{report.report ?? REPORT_FALLBACK}</p>
          </div>
        </section>

        <section className={styles.summaryGrid} aria-label="Indicadores do mes">
          <article className={styles.summaryCard}>
            <p className={styles.summaryLabel}>Total gasto</p>
            <p className={styles.summaryValue}>{formatCurrency(report.aggregations.totalOut)}</p>
            <p className={styles.summaryMeta}>Saidas concluidas do mes selecionado.</p>
          </article>

          <article className={styles.summaryCard}>
            <p className={styles.summaryLabel}>Total recebido</p>
            <p className={styles.summaryValue}>{formatCurrency(report.aggregations.totalIn)}</p>
            <p className={styles.summaryMeta}>
              {report.aggregations.transactionCount} transacoes consideradas no fecho mensal.
            </p>
          </article>

          <article className={styles.summaryCard}>
            <p className={styles.summaryLabel}>Vs. mes anterior</p>
            <p className={styles.summaryValue}>{previousDelta.label}</p>
            <p className={`${styles.delta} ${styles[previousDelta.tone]}`}>{previousDelta.detail}</p>
          </article>
        </section>

        <div className={styles.grid}>
          <section className={styles.column}>
            <section className={styles.panel}>
              <div className={styles.panelBody}>
                <div className={styles.sectionHeader}>
                  <div>
                    <h2 className={styles.sectionTitle}>Anomalias</h2>
                    <p className={styles.sectionDescription}>
                      Transacoes com comportamento acima do habitual neste mes.
                    </p>
                  </div>
                </div>

                {report.anomalies.length === 0 ? (
                  <div className={styles.emptyState}>
                    Nenhuma anomalia foi detetada para {formatMonthLabel(report.month)}.
                  </div>
                ) : (
                  <div className={styles.anomalyList}>
                    {report.anomalies.map((anomaly) => (
                      <article key={anomaly.transactionId} className={styles.anomalyCard}>
                        <div className={styles.anomalyHeader}>
                          <div>
                            <p className={styles.anomalyMerchant}>{getAnomalyTitle(anomaly)}</p>
                            <p className={styles.anomalyRaw}>{anomaly.rawDescription}</p>
                          </div>
                          <p className={styles.anomalyAmount}>{formatCurrency(anomaly.amount)}</p>
                        </div>
                        <div className={styles.anomalyMeta}>
                          <span>{anomaly.category ?? "Sem categoria"}</span>
                          <span>{formatDate(anomaly.bookingDate)}</span>
                          <span>{formatDeviation(anomaly.deviationFactor)}</span>
                        </div>
                      </article>
                    ))}
                  </div>
                )}
              </div>
            </section>
          </section>

          <section className={styles.column}>
            <section className={styles.panel}>
              <div className={styles.panelBody}>
                <div className={styles.sectionHeader}>
                  <div>
                    <h2 className={styles.sectionTitle}>Top categorias</h2>
                    <p className={styles.sectionDescription}>
                      Distribuicao do gasto mensal por categoria canonical.
                    </p>
                  </div>
                </div>
                <BarList
                  items={report.aggregations.topCategories.map((item) => ({
                    key: item.name,
                    label: item.name,
                    amount: item.totalOut,
                    meta: `${item.count} movimentos`,
                  }))}
                />
              </div>
            </section>

            <section className={styles.panel}>
              <div className={styles.panelBody}>
                <div className={styles.sectionHeader}>
                  <div>
                    <h2 className={styles.sectionTitle}>Top merchants</h2>
                    <p className={styles.sectionDescription}>
                      Comerciantes com maior peso nas saidas do mes.
                    </p>
                  </div>
                </div>
                <BarList
                  items={report.aggregations.topMerchants.map((item) => ({
                    key: item.name,
                    label: item.name,
                    amount: item.totalOut,
                    meta: `${item.count} movimentos`,
                  }))}
                />
              </div>
            </section>
          </section>
        </div>
      </div>
    </div>
  );
}

type BarListProps = {
  items: Array<{
    key: string;
    label: string;
    amount: number;
    meta: string;
  }>;
};

function BarList({ items }: BarListProps) {
  const peak = items[0]?.amount ?? 0;

  if (items.length === 0) {
    return <div className={styles.emptyState}>Sem dados suficientes para este grafico.</div>;
  }

  return (
    <div className={styles.chartList}>
      {items.map((item) => {
        const width = peak > 0 ? `${Math.max((item.amount / peak) * 100, 6)}%` : "0%";

        return (
          <div key={item.key} className={styles.chartRow}>
            <div className={styles.chartLabelWrap}>
              <p className={styles.chartLabel}>{item.label}</p>
              <p className={styles.chartMeta}>{item.meta}</p>
            </div>
            <div className={styles.chartTrack} aria-hidden="true">
              <div className={styles.chartBar} style={{ width }} />
            </div>
            <div className={styles.chartValue}>{formatCurrency(item.amount)}</div>
          </div>
        );
      })}
    </div>
  );
}

function last12Months() {
  const months: string[] = [];
  const today = new Date();

  for (let offset = 0; offset < 12; offset += 1) {
    const date = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth() - offset, 1));
    months.push(monthKey(date));
  }

  return months;
}

function monthKey(date: Date) {
  const year = date.getUTCFullYear();
  const month = String(date.getUTCMonth() + 1).padStart(2, "0");
  return `${year}-${month}`;
}

function formatMonthLabel(month: string) {
  const [year, numericMonth] = month.split("-").map(Number);
  return new Intl.DateTimeFormat("pt-PT", {
    month: "long",
    year: "numeric",
  }).format(new Date(Date.UTC(year, numericMonth - 1, 1)));
}

function formatCurrency(value: number) {
  return new Intl.NumberFormat("pt-PT", {
    style: "currency",
    currency: "EUR",
    maximumFractionDigits: 2,
  }).format(value);
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat("pt-PT", {
    day: "2-digit",
    month: "short",
    year: "numeric",
  }).format(new Date(`${value}T00:00:00Z`));
}

function formatDeviation(value: number) {
  return `x${value.toFixed(1)} acima do habitual`;
}

function getPreviousMonthDelta(current: number, previous: number | null) {
  if (previous === null) {
    return {
      label: "Sem dados",
      detail: "Nao existe base comparativa para o mes anterior.",
      tone: "neutral",
    } as const;
  }

  const delta = current - previous;
  const tone = delta > 0 ? "negative" : delta < 0 ? "positive" : "neutral";
  const prefix = delta > 0 ? "+" : "";

  return {
    label: `${prefix}${formatCurrency(delta)}`,
    detail: `Anterior: ${formatCurrency(previous)}`,
    tone,
  } as const;
}

function getAnomalyTitle(anomaly: MonthlyAnomaly) {
  return anomaly.normalizedMerchant?.trim() || anomaly.rawDescription;
}
