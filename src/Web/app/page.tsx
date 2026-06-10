"use client";

import { useDeferredValue, useEffect, useMemo, useState } from "react";
import styles from "./dashboard.module.css";
import { SpendingChart } from "./components/SpendingChart";
import { SummaryCards } from "./components/SummaryCards";
import { TransactionList } from "./components/TransactionList";
import {
  getAccounts,
  getSpendingByCategory,
  getTransactions,
  type AccountBalance,
  type CategorySpend,
  type DashboardTransaction,
} from "./lib/api";

export default function HomePage() {
  const [selectedMonth, setSelectedMonth] = useState(() => monthKey(new Date()));
  const [selectedAccount, setSelectedAccount] = useState("");
  const [selectedCategory, setSelectedCategory] = useState("");
  const [search, setSearch] = useState("");
  const deferredSearch = useDeferredValue(search.trim());

  const [accounts, setAccounts] = useState<AccountBalance[]>([]);
  const [spending, setSpending] = useState<CategorySpend[]>([]);
  const [transactions, setTransactions] = useState<DashboardTransaction[]>([]);
  const [monthTransactions, setMonthTransactions] = useState<DashboardTransaction[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function loadSummary() {
      try {
        setError(null);
        setLoading(true);

        const [accountsResponse, spendingResponse, monthTransactionsResponse] = await Promise.all([
          getAccounts(),
          getSpendingByCategory(selectedMonth),
          getTransactions({ month: selectedMonth }),
        ]);

        if (cancelled) {
          return;
        }

        setAccounts(accountsResponse);
        setSpending(spendingResponse.categories);
        setMonthTransactions(monthTransactionsResponse);
      } catch (loadError) {
        if (!cancelled) {
          setError(loadError instanceof Error ? loadError.message : "Failed to load dashboard");
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    void loadSummary();

    return () => {
      cancelled = true;
    };
  }, [selectedMonth]);

  useEffect(() => {
    let cancelled = false;

    async function loadTransactions() {
      try {
        setError(null);

        const response = await getTransactions({
          month: selectedMonth,
          account: selectedAccount || undefined,
          category: selectedCategory || undefined,
          search: deferredSearch || undefined,
        });

        if (!cancelled) {
          setTransactions(response);
        }
      } catch (loadError) {
        if (!cancelled) {
          setError(loadError instanceof Error ? loadError.message : "Failed to load transactions");
        }
      }
    }

    void loadTransactions();

    return () => {
      cancelled = true;
    };
  }, [deferredSearch, selectedAccount, selectedCategory, selectedMonth]);

  useEffect(() => {
    setSelectedCategory("");
    setSelectedAccount("");
    setSearch("");
  }, [selectedMonth]);

  const availableCategories = useMemo(
    () =>
      Array.from(new Set(monthTransactions.map((transaction) => transaction.category))).sort((a, b) =>
        a.localeCompare(b, "pt-PT"),
      ),
    [monthTransactions],
  );

  const accountOptions = useMemo(
    () =>
      accounts.map((account) => ({
        id: String(account.id),
        label: `${account.name} · ${account.source}`,
      })),
    [accounts],
  );

  const totalSpent = spending.reduce((sum, item) => sum + item.total, 0);
  const monthLabel = formatMonthLabel(selectedMonth);
  const preferredCurrency = accounts[0]?.currency ?? "EUR";

  if (loading) {
    return (
      <main className={styles.loading}>
        <div className={styles.loadingCard}>
          <h1 className={styles.sectionTitle}>A carregar dashboard</h1>
          <p className={styles.sectionDescription}>
            A sincronizar saldos, categorias e transacoes reais do mes selecionado.
          </p>
        </div>
      </main>
    );
  }

  if (error) {
    return (
      <main className={styles.error}>
        <div className={styles.errorCard}>
          <h1 className={styles.sectionTitle}>Falha ao carregar o dashboard</h1>
          <p className={styles.sectionDescription}>{error}</p>
          <button type="button" onClick={() => window.location.reload()}>
            Tentar novamente
          </button>
        </div>
      </main>
    );
  }

  return (
    <main className={styles.page}>
      <div className={styles.shell}>
        <header className={styles.hero}>
          <p className={styles.eyebrow}>Financial App · overview</p>
          <div className={styles.titleRow}>
            <div>
              <h1 className={styles.title}>Contas, consumo e detalhe.</h1>
              <p className={styles.subtitle}>
                Vista principal com saldo por conta, gasto mensal por categoria e leitura
                transacional completa, incluindo merchants normalizados e descricao original.
              </p>
            </div>
            <select
              className={styles.monthSelect}
              value={selectedMonth}
              onChange={(event) => setSelectedMonth(event.target.value)}
              aria-label="Selecionar mes"
            >
              {last12Months().map((month) => (
                <option key={month} value={month}>
                  {formatMonthLabel(month)}
                </option>
              ))}
            </select>
          </div>
        </header>

        <div className={styles.grid}>
          <div className={styles.column}>
            <SummaryCards accounts={accounts} totalSpent={totalSpent} monthLabel={monthLabel} />
            <TransactionList
              transactions={transactions}
              accounts={accountOptions}
              categories={availableCategories}
              selectedAccount={selectedAccount}
              selectedCategory={selectedCategory}
              search={search}
              onAccountChange={setSelectedAccount}
              onCategoryChange={setSelectedCategory}
              onSearchChange={setSearch}
            />
          </div>

          <div className={styles.column}>
            <SpendingChart categories={spending} currency={preferredCurrency} />
          </div>
        </div>
      </div>
    </main>
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
