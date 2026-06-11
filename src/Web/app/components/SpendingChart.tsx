import styles from "../dashboard.module.css";
import { formatCurrency } from "../lib/format";
import type { CategorySpend } from "../lib/api";

type SpendingChartProps = {
  categories: CategorySpend[];
  currency: string;
};

export function SpendingChart({ categories, currency }: SpendingChartProps) {
  const peak = categories[0]?.total ?? 0;

  return (
    <section className={styles.panel}>
      <div className={styles.panelBody}>
        <div className={styles.sectionHeader}>
          <div>
            <h2 className={styles.sectionTitle}>Gastos por categoria</h2>
            <p className={styles.sectionDescription}>
              Leitura mensal de saidas agrupadas pela categoria canonical.
            </p>
          </div>
        </div>

        {categories.length === 0 ? (
          <div className={styles.emptyState}>Nao existem gastos concluidos para o mes selecionado.</div>
        ) : (
          <div className={styles.chartList}>
            {categories.map((item) => {
              const width = peak > 0 ? `${Math.max((item.total / peak) * 100, 6)}%` : "0%";

              return (
                <div key={item.category} className={styles.chartRow}>
                  <p className={styles.chartLabel}>{item.category}</p>
                  <div className={styles.chartTrack} aria-hidden="true">
                    <div className={styles.chartBar} style={{ width }} />
                  </div>
                  <div className={styles.chartValue}>{formatCurrency(item.total, currency)}</div>
                </div>
              );
            })}
          </div>
        )}
      </div>
    </section>
  );
}

