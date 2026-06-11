"use client";

import { useDeferredValue, useEffect, useMemo, useRef, useState } from "react";
import { KPICard } from "./components/KPICard";
import { CategorySpendChart } from "./components/CategorySpendChart";
import { MonthlyTrendChart } from "./components/MonthlyTrendChart";
import { TransactionTablePremium } from "./components/TransactionTablePremium";
import { EditTransactionCategoryModal } from "./components/EditTransactionCategoryModal";
import { DateRangeSelector } from "./components/DateRangeSelector";
import { formatMonthLabel, monthKey, getDateRangeMonths, getDateRangeFromMonths, getAllAvailableMonths } from "./lib/format";
import {
  getAccounts,
  getCategories,
  getSpendingByCategory,
  getTransactions,
  getMonthlyTrend,
  type AccountBalance,
  type CategorySpend,
  type DashboardTransaction,
  type MonthlyTrendData,
} from "./lib/api";

export default function HomePage() {
  const [selectedMonth, setSelectedMonth] = useState(() => monthKey(new Date()));
  const [transactionDateRangeMonths, setTransactionDateRangeMonths] = useState(() =>
    getDateRangeMonths(null, undefined)
  );
  const [transactionRangeOverride, setTransactionRangeOverride] = useState<string | null>(null);
  const [trendRangeMonths, setTrendRangeMonths] = useState(() => getDateRangeMonths(null, "1y"));
  const [selectedAccount, setSelectedAccount] = useState("");
  const [selectedCategory, setSelectedCategory] = useState("");
  const [search, setSearch] = useState("");
  const deferredSearch = useDeferredValue(search.trim());

  const [accounts, setAccounts] = useState<AccountBalance[]>([]);
  const [categories, setCategories] = useState<Array<{ id: number; name: string }>>([]);
  const [spending, setSpending] = useState<CategorySpend[]>([]);
  const [monthTotals, setMonthTotals] = useState<{ credits: number; debits: number }>({ credits: 0, debits: 0 });
  const [monthTransactions, setMonthTransactions] = useState<DashboardTransaction[]>([]);
  const [transactions, setTransactions] = useState<DashboardTransaction[]>([]);
  const [monthlyTrend, setMonthlyTrend] = useState<MonthlyTrendData[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editingTransaction, setEditingTransaction] = useState<{ id: number; merchant: string } | null>(null);
  const transactionsSectionRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    let cancelled = false;

    async function loadSummary() {
      try {
        setError(null);
        setLoading(true);

        const [accountsResponse, spendingResponse, monthTransactionsResponse, categoriesResponse] = await Promise.all([
          getAccounts(),
          getSpendingByCategory(selectedMonth),
          getTransactions({ month: selectedMonth }),
          getCategories(),
        ]);

        if (cancelled) {
          return;
        }

        setAccounts(accountsResponse);
        setSpending(spendingResponse.categories);
        setMonthTotals(spendingResponse.totals ?? { credits: 0, debits: 0 });
        setMonthTransactions(monthTransactionsResponse);
        setCategories(categoriesResponse);
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

  // Evolução Mensal tem filtro de período próprio
  useEffect(() => {
    let cancelled = false;

    async function loadTrend() {
      try {
        const range = getDateRangeFromMonths(trendRangeMonths);
        const response = await getMonthlyTrend(range ?? undefined);
        if (!cancelled) {
          setMonthlyTrend(response);
        }
      } catch (loadError) {
        if (!cancelled) {
          setError(loadError instanceof Error ? loadError.message : "Failed to load monthly trend");
        }
      }
    }

    void loadTrend();

    return () => {
      cancelled = true;
    };
  }, [trendRangeMonths]);

  useEffect(() => {
    let cancelled = false;

    async function loadTransactions() {
      try {
        setError(null);

        const dateRange = getDateRangeFromMonths(transactionDateRangeMonths);

        const response = await getTransactions({
          fromDate: dateRange?.fromDate,
          toDate: dateRange?.toDate,
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
  }, [deferredSearch, selectedAccount, selectedCategory, transactionDateRangeMonths]);

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

  const totalBalance = accounts.reduce((sum, acc) => sum + acc.balance, 0);
  const preferredCurrency = accounts[0]?.currency ?? "EUR";
  const monthNet = monthTotals.credits - monthTotals.debits;

  const formatCurrency = (value: number) =>
    value.toLocaleString("pt-PT", {
      style: "currency",
      currency: preferredCurrency,
      minimumFractionDigits: 0,
      maximumFractionDigits: 0,
    });

  // Clicar numa categoria do gráfico filtra a tabela de transações para esse mês + categoria
  const handleCategoryClick = (category: string) => {
    if (selectedCategory === category) {
      setSelectedCategory("");
      return;
    }

    setSelectedCategory(category);
    setTransactionDateRangeMonths([selectedMonth]);
    setTransactionRangeOverride(formatMonthLabel(selectedMonth));
    transactionsSectionRef.current?.scrollIntoView({ behavior: "smooth", block: "start" });
  };

  if (loading) {
    return (
      <div
        className="min-h-screen flex flex-col items-center justify-center px-4"
        style={{ backgroundColor: "var(--bg-primary)" }}
      >
        <div
          className="rounded-xl p-8 border text-center"
          style={{
            backgroundColor: "var(--bg-secondary)",
            borderColor: "var(--border)",
          }}
        >
          <div className="mb-4 inline-block">
            <div
              className="w-12 h-12 rounded-lg animate-spin"
              style={{
                borderTop: "3px solid var(--accent)",
                borderRight: "3px solid transparent",
              }}
            />
          </div>
          <h1 style={{ color: "var(--text-primary)", marginTop: 0 }} className="text-lg font-semibold">
            A carregar dashboard
          </h1>
          <p style={{ color: "var(--text-tertiary)" }} className="text-sm">
            A sincronizar saldos, categorias e transações do mês selecionado.
          </p>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div
        className="min-h-screen flex flex-col items-center justify-center px-4"
        style={{ backgroundColor: "var(--bg-primary)" }}
      >
        <div
          className="rounded-xl p-8 border text-center max-w-md"
          style={{
            backgroundColor: "var(--bg-secondary)",
            borderColor: "var(--border)",
          }}
        >
          <h1 style={{ color: "var(--error)", marginTop: 0 }} className="text-lg font-semibold">
            Falha ao carregar o dashboard
          </h1>
          <p style={{ color: "var(--text-secondary)" }} className="text-sm mb-6">
            {error}
          </p>
          <button
            onClick={() => window.location.reload()}
            className="px-6 py-2 rounded-lg font-medium transition-all hover:opacity-90"
            style={{
              backgroundColor: "var(--accent)",
              color: "white",
            }}
          >
            Tentar novamente
          </button>
        </div>
      </div>
    );
  }

  return (
    <div
      className="min-h-screen"
      style={{ backgroundColor: "var(--bg-primary)" }}
    >
      <div className="max-w-7xl mx-auto px-6 py-8">
        {/* Header */}
        <div className="flex flex-col gap-6 mb-8">
          <div className="flex items-end justify-between">
            <div>
              <h1 style={{ color: "var(--text-primary)", marginTop: 0, marginBottom: 8 }} className="text-4xl font-bold">
                Dashboard Financeiro
              </h1>
              <p style={{ color: "var(--text-secondary)", marginTop: 0 }} className="text-sm">
                Visão consolidada de contas, gastos e transações.
              </p>
            </div>
            <div>
              <label style={{ color: "var(--text-tertiary)" }} className="block text-xs font-medium mb-2">
                Mês em análise
              </label>
              <select
                value={selectedMonth}
                onChange={(event) => setSelectedMonth(event.target.value)}
                className="px-4 py-2 rounded-lg border text-sm font-medium transition-colors"
                style={{
                  backgroundColor: "var(--bg-secondary)",
                  borderColor: "var(--border)",
                  color: "var(--text-primary)",
                }}
                aria-label="Selecionar mês em análise"
              >
                {getAllAvailableMonths().map((month) => (
                  <option key={month} value={month}>
                    {formatMonthLabel(month)}
                  </option>
                ))}
              </select>
            </div>
          </div>
        </div>

        {/* KPI Cards — dados reais do mês em análise */}
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mb-8">
          <KPICard
            label="Saldo Total"
            value={formatCurrency(totalBalance)}
            icon="💳"
          />
          <KPICard
            label="Receitas do Mês"
            value={<span style={{ color: "var(--success)" }}>{formatCurrency(monthTotals.credits)}</span>}
            icon="📈"
          />
          <KPICard
            label="Gastos do Mês"
            value={<span style={{ color: "var(--error)" }}>{formatCurrency(monthTotals.debits)}</span>}
            icon="💸"
          />
          <KPICard
            label="Resultado do Mês"
            value={
              <span style={{ color: monthNet >= 0 ? "var(--success)" : "var(--error)" }}>
                {monthNet >= 0 ? "+" : ""}
                {formatCurrency(monthNet)}
              </span>
            }
            icon="⚖️"
          />
        </div>

        {/* Charts Row */}
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 mb-8">
          <MonthlyTrendChart
            data={monthlyTrend}
            toolbar={
              <DateRangeSelector
                initialPreset="1y"
                onMonthsChange={setTrendRangeMonths}
              />
            }
          />
          <CategorySpendChart
            categories={spending}
            selectedCategory={selectedCategory || undefined}
            onCategoryClick={handleCategoryClick}
          />
        </div>

        {/* Transactions Table */}
        <div ref={transactionsSectionRef} className="scroll-mt-6">
          <div className="flex items-center justify-between mb-3">
            <h2 style={{ color: "var(--text-primary)", margin: 0 }} className="text-lg font-semibold">
              Transações
            </h2>
            <DateRangeSelector
              onMonthsChange={(months) => {
                setTransactionRangeOverride(null);
                setTransactionDateRangeMonths(months);
              }}
              overrideLabel={transactionRangeOverride ?? undefined}
            />
          </div>
          <TransactionTablePremium
            transactions={transactions}
            search={search}
            onSearchChange={setSearch}
            selectedCategory={selectedCategory}
            onCategoryChange={setSelectedCategory}
            selectedAccount={selectedAccount}
            onAccountChange={setSelectedAccount}
            categories={availableCategories}
            accounts={accountOptions}
            onEditCategory={(txn) => setEditingTransaction({ id: txn.id, merchant: txn.normalizedMerchant || txn.rawDescription })}
          />
        </div>

        {/* Edit Category Modal */}
        {editingTransaction && (
          <EditTransactionCategoryModal
            transactionId={editingTransaction.id}
            currentCategory={transactions.find((t) => t.id === editingTransaction.id)?.category || ""}
            merchant={editingTransaction.merchant}
            categories={categories}
            onClose={() => setEditingTransaction(null)}
            onSuccess={() => {
              setEditingTransaction(null);
              // Reload transactions
              void (async () => {
                const response = await getTransactions({
                  month: selectedMonth,
                  account: selectedAccount || undefined,
                  category: selectedCategory || undefined,
                  search: deferredSearch || undefined,
                });
                setTransactions(response);
              })();
            }}
          />
        )}
      </div>
    </div>
  );
}

