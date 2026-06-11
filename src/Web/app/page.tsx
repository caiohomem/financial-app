"use client";

import { useDeferredValue, useEffect, useMemo, useState } from "react";
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
  const [selectedAccount, setSelectedAccount] = useState("");
  const [selectedCategory, setSelectedCategory] = useState("");
  const [search, setSearch] = useState("");
  const deferredSearch = useDeferredValue(search.trim());

  const [accounts, setAccounts] = useState<AccountBalance[]>([]);
  const [categories, setCategories] = useState<Array<{ id: number; name: string }>>([]);
  const [spending, setSpending] = useState<CategorySpend[]>([]);
  const [monthTransactions, setMonthTransactions] = useState<DashboardTransaction[]>([]);
  const [transactions, setTransactions] = useState<DashboardTransaction[]>([]);
  const [monthlyTrend, setMonthlyTrend] = useState<MonthlyTrendData[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editingTransaction, setEditingTransaction] = useState<{ id: number; merchant: string } | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function loadSummary() {
      try {
        setError(null);
        setLoading(true);

        const [accountsResponse, spendingResponse, monthTransactionsResponse, categoriesResponse, trendResponse] = await Promise.all([
          getAccounts(),
          getSpendingByCategory(selectedMonth),
          getTransactions({ month: selectedMonth }),
          getCategories(),
          getMonthlyTrend(),
        ]);

        if (cancelled) {
          return;
        }

        setAccounts(accountsResponse);
        setSpending(spendingResponse.categories);
        setMonthTransactions(monthTransactionsResponse);
        setCategories(categoriesResponse);
        setMonthlyTrend(trendResponse);
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

  const totalSpent = spending.reduce((sum, item) => sum + item.total, 0);
  const totalBalance = accounts.reduce((sum, acc) => sum + acc.balance, 0);
  const preferredCurrency = accounts[0]?.currency ?? "EUR";

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
            <div className="flex gap-4 items-end">
              <div>
                <label style={{ color: "var(--text-tertiary)" }} className="block text-xs font-medium mb-2">
                  KPI / Gráficos
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
                  aria-label="Selecionar mês para KPI"
                >
                  {getAllAvailableMonths().map((month) => (
                    <option key={month} value={month}>
                      {formatMonthLabel(month)}
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label style={{ color: "var(--text-tertiary)" }} className="block text-xs font-medium mb-2">
                  Transações
                </label>
                <DateRangeSelector
                  onMonthsChange={setTransactionDateRangeMonths}
                />
              </div>
            </div>
          </div>
        </div>

        {/* KPI Cards */}
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mb-8">
          <KPICard
            label="Saldo Total"
            value={totalBalance.toLocaleString("pt-PT", {
              style: "currency",
              currency: preferredCurrency,
              minimumFractionDigits: 0,
              maximumFractionDigits: 0,
            })}
            trend={{ direction: "up", percentage: 12 }}
            icon="💳"
          />
          <KPICard
            label="Gastos do Mês"
            value={totalSpent.toLocaleString("pt-PT", {
              style: "currency",
              currency: preferredCurrency,
              minimumFractionDigits: 0,
              maximumFractionDigits: 0,
            })}
            trend={{ direction: "down", percentage: 8 }}
            icon="💸"
          />
          <KPICard
            label="Categorias Ativas"
            value={availableCategories.length}
            icon="📊"
          />
          <KPICard
            label="Transações"
            value={monthTransactions.length}
            icon="📋"
          />
        </div>

        {/* Charts Row */}
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 mb-8">
          <MonthlyTrendChart
            data={monthlyTrend}
          />
          <CategorySpendChart categories={spending} />
        </div>

        {/* Transactions Table */}
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

