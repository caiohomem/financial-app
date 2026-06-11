"use client";

import { useMemo } from "react";
import type { DashboardTransaction, Category } from "../lib/api";

type TableProps = {
  transactions: DashboardTransaction[];
  search: string;
  onSearchChange: (value: string) => void;
  selectedCategory: string;
  onCategoryChange: (value: string) => void;
  selectedAccount: string;
  onAccountChange: (value: string) => void;
  categories: string[];
  accounts: Array<{ id: string; label: string }>;
  availableCategories?: Category[];
  onEditCategory?: (transaction: DashboardTransaction) => void;
};

const CATEGORY_COLORS: Record<string, string> = {
  Alimentação: "#F59E0B",
  Transportes: "#3B82F6",
  Saúde: "#22C55E",
  Entretenimento: "#EC4899",
  Trabalho: "#8B5CF6",
  Compras: "#14B8A6",
  Utilidades: "#EF4444",
  Outros: "#64748B",
};

export function TransactionTablePremium({
  transactions,
  search,
  onSearchChange,
  selectedCategory,
  onCategoryChange,
  selectedAccount,
  onAccountChange,
  categories,
  accounts,
  onEditCategory,
}: TableProps) {
  const filtered = useMemo(() => {
    return transactions.filter((t) => {
      const searchMatch = !search ||
        t.rawDescription.toLowerCase().includes(search.toLowerCase()) ||
        (t.normalizedMerchant?.toLowerCase() || "").includes(search.toLowerCase());

      const categoryMatch = !selectedCategory || t.category === selectedCategory;
      const accountMatch = !selectedAccount || `${t.accountName} · ${t.source}` === accounts.find(a => a.id === selectedAccount)?.label;

      return searchMatch && categoryMatch && accountMatch;
    });
  }, [transactions, search, selectedCategory, selectedAccount, accounts]);

  return (
    <div
      className="rounded-xl border"
      style={{
        backgroundColor: "var(--bg-secondary)",
        borderColor: "var(--border)",
      }}
    >
      {/* Filtros */}
      <div className="p-6 border-b" style={{ borderColor: "var(--border)" }}>
        <div className="flex flex-col gap-4">
          <div>
            <input
              type="text"
              placeholder="Buscar transação, merchant…"
              value={search}
              onChange={(e) => onSearchChange(e.target.value)}
              className="w-full px-4 py-2 rounded-lg border text-sm transition-colors"
              style={{
                backgroundColor: "var(--bg-tertiary)",
                borderColor: "var(--border)",
                color: "var(--text-primary)",
              }}
              onFocus={(e) => {
                e.currentTarget.style.borderColor = "var(--accent)";
                e.currentTarget.style.backgroundColor = "var(--bg-primary)";
              }}
              onBlur={(e) => {
                e.currentTarget.style.borderColor = "var(--border)";
                e.currentTarget.style.backgroundColor = "var(--bg-tertiary)";
              }}
            />
          </div>

          <div className="flex gap-3">
            <select
              value={selectedAccount}
              onChange={(e) => onAccountChange(e.target.value)}
              className="flex-1 px-4 py-2 rounded-lg border text-sm transition-colors"
              style={{
                backgroundColor: "var(--bg-tertiary)",
                borderColor: "var(--border)",
                color: "var(--text-primary)",
              }}
            >
              <option value="">Todas as contas</option>
              {accounts.map((acc) => (
                <option key={acc.id} value={acc.id}>
                  {acc.label}
                </option>
              ))}
            </select>

            <select
              value={selectedCategory}
              onChange={(e) => onCategoryChange(e.target.value)}
              className="flex-1 px-4 py-2 rounded-lg border text-sm transition-colors"
              style={{
                backgroundColor: "var(--bg-tertiary)",
                borderColor: "var(--border)",
                color: "var(--text-primary)",
              }}
            >
              <option value="">Todas as categorias</option>
              {categories.map((cat) => (
                <option key={cat} value={cat}>
                  {cat}
                </option>
              ))}
            </select>
          </div>
        </div>
      </div>

      {/* Tabela */}
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr style={{ borderColor: "var(--border)", borderBottomWidth: "1px" }}>
              <th className="px-6 py-3 text-left font-semibold" style={{ color: "var(--text-tertiary)" }}>
                Data
              </th>
              <th className="px-6 py-3 text-left font-semibold" style={{ color: "var(--text-tertiary)" }}>
                Merchant
              </th>
              <th className="px-6 py-3 text-left font-semibold" style={{ color: "var(--text-tertiary)" }}>
                Descrição
              </th>
              <th className="px-6 py-3 text-left font-semibold" style={{ color: "var(--text-tertiary)" }}>
                Categoria
              </th>
              <th className="px-6 py-3 text-left font-semibold" style={{ color: "var(--text-tertiary)" }}>
                Conta
              </th>
              <th className="px-6 py-3 text-right font-semibold" style={{ color: "var(--text-tertiary)" }}>
                Valor
              </th>
              {onEditCategory && (
                <th className="px-6 py-3 text-center font-semibold" style={{ color: "var(--text-tertiary)" }}>
                  Ações
                </th>
              )}
            </tr>
          </thead>
          <tbody>
            {filtered.length === 0 ? (
              <tr>
                <td colSpan={6} className="px-6 py-12 text-center">
                  <p style={{ color: "var(--text-tertiary)" }}>Nenhuma transação encontrada</p>
                </td>
              </tr>
            ) : (
              filtered.map((txn) => (
                <tr
                  key={txn.id}
                  className="border-b transition-colors hover:bg-opacity-50"
                  style={{
                    borderColor: "var(--border)",
                  }}
                  onMouseEnter={(e) => {
                    e.currentTarget.style.backgroundColor = "var(--bg-tertiary)";
                  }}
                  onMouseLeave={(e) => {
                    e.currentTarget.style.backgroundColor = "transparent";
                  }}
                >
                  <td className="px-6 py-4" style={{ color: "var(--text-secondary)" }}>
                    {new Date(txn.bookingDate + "T00:00:00Z").toLocaleDateString("pt-PT", {
                      day: "2-digit",
                      month: "short",
                      year: "numeric",
                    })}
                  </td>
                  <td className="px-6 py-4" style={{ color: "var(--text-primary)" }}>
                    <div className="font-medium">
                      {txn.normalizedMerchant || "—"}
                    </div>
                  </td>
                  <td className="px-6 py-4" style={{ color: "var(--text-secondary)" }}>
                    <div className="max-w-xs truncate text-xs">
                      {txn.rawDescription}
                    </div>
                  </td>
                  <td className="px-6 py-4">
                    <span
                      className="px-2 py-1 rounded-full text-xs font-medium"
                      style={{
                        backgroundColor: CATEGORY_COLORS[txn.category]
                          ? `${CATEGORY_COLORS[txn.category]}20`
                          : "var(--border)",
                        color: CATEGORY_COLORS[txn.category] || "var(--text-tertiary)",
                      }}
                    >
                      {txn.category}
                    </span>
                  </td>
                  <td className="px-6 py-4" style={{ color: "var(--text-secondary)" }}>
                    <div className="text-xs">{txn.accountName}</div>
                  </td>
                  <td className="px-6 py-4 text-right font-semibold" style={{
                    color: txn.direction === "IN" ? "var(--success)" : "var(--text-primary)"
                  }}>
                    {txn.direction === "IN" ? "+" : "−"}
                    {Math.abs(txn.amount).toLocaleString("pt-PT", {
                      minimumFractionDigits: 2,
                      maximumFractionDigits: 2,
                    })}
                  </td>
                  {onEditCategory && (
                    <td className="px-6 py-4 text-center">
                      <button
                        onClick={() => onEditCategory(txn)}
                        className="px-3 py-1 rounded text-xs font-medium transition-all"
                        style={{
                          backgroundColor: "rgba(59, 130, 246, 0.1)",
                          color: "var(--accent)",
                          border: "1px solid var(--accent)",
                        }}
                        onMouseEnter={(e) => {
                          e.currentTarget.style.backgroundColor = "var(--accent)";
                          e.currentTarget.style.color = "white";
                        }}
                        onMouseLeave={(e) => {
                          e.currentTarget.style.backgroundColor = "rgba(59, 130, 246, 0.1)";
                          e.currentTarget.style.color = "var(--accent)";
                        }}
                      >
                        Editar
                      </button>
                    </td>
                  )}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
