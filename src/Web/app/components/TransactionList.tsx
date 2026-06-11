import Link from "next/link";
import styles from "../dashboard.module.css";
import ui from "../styles/ui.module.css";
import { formatCurrency, formatDate } from "../lib/format";
import type { DashboardTransaction } from "../lib/api";

type TransactionListProps = {
  transactions: DashboardTransaction[];
  accounts: Array<{ id: string; label: string }>;
  categories: string[];
  selectedAccount: string;
  selectedCategory: string;
  search: string;
  onAccountChange: (value: string) => void;
  onCategoryChange: (value: string) => void;
  onSearchChange: (value: string) => void;
};

export function TransactionList({
  transactions,
  accounts,
  categories,
  selectedAccount,
  selectedCategory,
  search,
  onAccountChange,
  onCategoryChange,
  onSearchChange,
}: TransactionListProps) {
  return (
    <section className={styles.panel}>
      <div className={styles.panelBody}>
        <div className={styles.sectionHeader}>
          <div>
            <h2 className={styles.sectionTitle}>Transacoes</h2>
            <p className={styles.sectionDescription}>
              Pesquisa por merchant e filtros combinaveis por conta, categoria e mes.
            </p>
          </div>
          <span className={styles.sourceBadge}>{transactions.length} itens</span>
        </div>

        <div className={styles.filters}>
          <div className={styles.filterField}>
            <label htmlFor="account-filter">Conta</label>
            <select
              id="account-filter"
              value={selectedAccount}
              onChange={(event) => onAccountChange(event.target.value)}
            >
              <option value="">Todas</option>
              {accounts.map((account) => (
                <option key={account.id} value={account.id}>
                  {account.label}
                </option>
              ))}
            </select>
          </div>

          <div className={styles.filterField}>
            <label htmlFor="category-filter">Categoria</label>
            <select
              id="category-filter"
              value={selectedCategory}
              onChange={(event) => onCategoryChange(event.target.value)}
            >
              <option value="">Todas</option>
              {categories.map((category) => (
                <option key={category} value={category}>
                  {category}
                </option>
              ))}
            </select>
          </div>

          <div className={`${styles.filterField} ${styles.filterFieldWide}`}>
            <label htmlFor="merchant-filter">Merchant</label>
            <input
              id="merchant-filter"
              type="search"
              placeholder="Spotify, Uber, Glovo..."
              value={search}
              onChange={(event) => onSearchChange(event.target.value)}
            />
          </div>
        </div>

        {transactions.length === 0 ? (
          <div className={ui.emptyState}>
            Nenhuma transação corresponde aos filtros selecionados.
            {accounts.length === 0 ? (
              <>
                {" "}
                <Link href="/import">Importe um extrato</Link> para começar.
              </>
            ) : null}
          </div>
        ) : (
          <div className={styles.tableWrap}>
            <table className={styles.table}>
              <thead>
                <tr>
                  <th>Data</th>
                  <th>Merchant</th>
                  <th>Categoria</th>
                  <th>Conta</th>
                  <th>Direcao</th>
                  <th>Montante</th>
                  <th>Estado</th>
                </tr>
              </thead>
              <tbody>
                {transactions.map((transaction) => {
                  const isCancelled = transaction.status === "cancelled";
                  const amountClass =
                    transaction.direction === "OUT" ? styles.amountOut : styles.amountIn;

                  return (
                    <tr
                      key={transaction.id}
                      className={isCancelled ? styles.mutedRow : undefined}
                    >
                      <td>{formatDate(transaction.bookingDate)}</td>
                      <td>
                        <div className={styles.merchantCell}>
                          <span
                            className={styles.merchantName}
                            title={transaction.rawDescription}
                          >
                            {transaction.normalizedMerchant}
                          </span>
                          <span className={styles.merchantRaw} title={transaction.rawDescription}>
                            {transaction.rawDescription}
                          </span>
                        </div>
                      </td>
                      <td>{transaction.category}</td>
                      <td>
                        <div className={styles.merchantCell}>
                          <span>{transaction.accountName}</span>
                          <span className={styles.merchantRaw}>{transaction.source}</span>
                        </div>
                      </td>
                      <td>{transaction.direction}</td>
                      <td className={amountClass}>
                        {transaction.direction === "OUT" ? "-" : "+"}
                        {formatCurrency(transaction.amount, transaction.currency)}
                      </td>
                      <td>
                        <span className={`${styles.statusBadge} ${statusClassName(transaction.status)}`}>
                          {transaction.status}
                        </span>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </section>
  );
}

function statusClassName(status: DashboardTransaction["status"]) {
  switch (status) {
    case "completed":
      return styles.statusBadgeCompleted;
    case "pending":
      return styles.statusBadgePending;
    case "cancelled":
      return styles.statusBadgeCancelled;
    case "refunded":
      return styles.statusBadgeRefunded;
    default:
      return "";
  }
}
