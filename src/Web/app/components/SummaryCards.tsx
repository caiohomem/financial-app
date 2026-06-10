import styles from "../dashboard.module.css";
import type { AccountBalance } from "../lib/api";

type SummaryCardsProps = {
  accounts: AccountBalance[];
  totalSpent: number;
  monthLabel: string;
};

export function SummaryCards({ accounts, totalSpent, monthLabel }: SummaryCardsProps) {
  const preferredCurrency = accounts[0]?.currency ?? "EUR";

  return (
    <section className={styles.panel}>
      <div className={styles.panelBody}>
        <div className={styles.sectionHeader}>
          <div>
            <h2 className={styles.sectionTitle}>Saldo por conta</h2>
            <p className={styles.sectionDescription}>
              Balancos reais importados, excluindo transacoes canceladas dos totais.
            </p>
          </div>
          <span className={styles.sourceBadge}>{monthLabel}</span>
        </div>

        <div className={styles.summaryGrid}>
          <article className={styles.summaryCard}>
            <p className={styles.summaryLabel}>Total gasto no mes</p>
            <p className={styles.summaryValue}>{formatCurrency(totalSpent, preferredCurrency)}</p>
            <div className={styles.summaryMeta}>
              <span>Saidas concluidas</span>
              <span>{monthLabel}</span>
            </div>
          </article>

          {accounts.map((account) => (
            <article key={account.id} className={styles.summaryCard}>
              <p className={styles.summaryLabel}>{account.name}</p>
              <p className={styles.summaryValue}>{formatCurrency(account.balance, account.currency)}</p>
              <div className={styles.summaryMeta}>
                <span>{account.currency}</span>
                <span className={styles.sourceBadge}>{account.source}</span>
              </div>
            </article>
          ))}
        </div>
      </div>
    </section>
  );
}

function formatCurrency(value: number, currency: string) {
  return new Intl.NumberFormat("pt-PT", {
    style: "currency",
    currency,
    maximumFractionDigits: 2,
  }).format(value);
}
