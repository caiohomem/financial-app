"use client";

import { useMemo, useState } from "react";
import type { Category, ReviewTransaction } from "../lib/api";
import { patchTransactionCategory } from "../lib/api";
import styles from "../review/review.module.css";

type PendingTransactionsListProps = {
  transactions: ReviewTransaction[];
  categories: Category[];
  onResolved: (transactionId: number) => void;
  onToast: (message: string) => void;
};

type MatchType = "merchant_eq" | "contains" | "regex";

export function PendingTransactionsList({
  transactions,
  categories,
  onResolved,
  onToast,
}: PendingTransactionsListProps) {
  const [selectedCategories, setSelectedCategories] = useState<Record<number, string>>({});
  const [promptRowId, setPromptRowId] = useState<number | null>(null);
  const [ruleFormRowId, setRuleFormRowId] = useState<number | null>(null);
  const [ruleDrafts, setRuleDrafts] = useState<Record<number, { pattern: string; matchType: MatchType }>>({});
  const [savingRowId, setSavingRowId] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  const categoryOptions = useMemo(
    () => categories.map((category) => ({ value: String(category.id), label: category.name })),
    [categories],
  );

  async function saveCategory(
    transaction: ReviewTransaction,
    createRule: boolean,
    draft?: { pattern: string; matchType: MatchType },
  ) {
    const selectedValue = selectedCategories[transaction.id] ?? String(transaction.categoryCanonicalId ?? "");
    const categoryId = Number(selectedValue);

    if (!Number.isInteger(categoryId)) {
      setError("Selecione uma categoria antes de guardar.");
      return;
    }

    try {
      setError(null);
      setSavingRowId(transaction.id);
      await patchTransactionCategory(transaction.id, {
        categoryId,
        createRule,
        pattern: createRule ? draft?.pattern.trim() : undefined,
        matchType: createRule ? draft?.matchType : undefined,
      });

      onResolved(transaction.id);
      onToast(
        createRule
          ? "Categoria revista e regra criada para futuras transacoes do merchant."
          : "Categoria revista com sucesso.",
      );
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Falha ao guardar categoria.");
    } finally {
      setSavingRowId(null);
      setPromptRowId(null);
      setRuleFormRowId(null);
    }
  }

  return (
    <>
      {error ? <div className={styles.errorBox}>{error}</div> : null}

      {transactions.length === 0 ? (
        <div className={styles.emptyState}>Nothing to review.</div>
      ) : (
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th>Data</th>
                <th>Merchant</th>
                <th>Descricao</th>
                <th>Montante</th>
                <th>Categoria atual</th>
                <th>Acao</th>
              </tr>
            </thead>
            <tbody>
              {transactions.map((transaction) => {
                const selectedValue =
                  selectedCategories[transaction.id] ?? String(transaction.categoryCanonicalId ?? "");
                const hasChange =
                  selectedValue !== String(transaction.categoryCanonicalId ?? "") && selectedValue !== "";
                const isSaving = savingRowId === transaction.id;
                const merchantLabel = transaction.normalizedMerchant ?? transaction.rawDescription;
                const draft = ruleDrafts[transaction.id] ?? {
                  pattern: transaction.normalizedMerchant ?? transaction.rawDescription,
                  matchType: "merchant_eq" as MatchType,
                };

                return (
                  <tr key={transaction.id}>
                    <td>{formatDate(transaction.date)}</td>
                    <td>
                      <div className={styles.merchantCell}>
                        <span className={styles.merchantName}>{merchantLabel}</span>
                        <span className={styles.merchantMeta}>Conta #{transaction.accountId}</span>
                      </div>
                    </td>
                    <td className={styles.merchantMeta}>{transaction.rawDescription}</td>
                    <td className={transaction.amount >= 0 ? styles.amountOut : styles.amountIn}>
                      {formatCurrency(transaction.amount, transaction.currency)}
                    </td>
                    <td>
                      <select
                        className={styles.select}
                        value={selectedValue}
                        onChange={(event) =>
                          setSelectedCategories((current) => ({
                            ...current,
                            [transaction.id]: event.target.value,
                          }))
                        }
                        disabled={isSaving}
                        aria-label={`Categoria para transacao ${transaction.id}`}
                      >
                        <option value="">Selecionar categoria</option>
                        {categoryOptions.map((category) => (
                          <option key={category.value} value={category.value}>
                            {category.label}
                          </option>
                        ))}
                      </select>
                    </td>
                    <td>
                      <div className={styles.actions}>
                        {hasChange ? (
                          <button
                            type="button"
                            className={styles.button}
                            onClick={() => {
                              setError(null);
                              setPromptRowId(transaction.id);
                              setRuleFormRowId(null);
                            }}
                            disabled={isSaving}
                          >
                            Save
                          </button>
                        ) : (
                          <span className={styles.merchantMeta}>
                            {transaction.categoryName ?? "Sem categoria"}
                          </span>
                        )}
                      </div>

                      {promptRowId === transaction.id ? (
                        <div className={styles.inlinePrompt}>
                          <p className={styles.promptTitle}>Confirmacao deliberada</p>
                          <p className={styles.promptText}>
                            Tambem quer criar uma regra para este merchant recategorizar futuras
                            transacoes?
                          </p>
                          <div className={styles.actions}>
                            <button
                              type="button"
                              className={styles.secondaryButton}
                              onClick={() =>
                                setRuleFormRowId((current) =>
                                  current === transaction.id ? null : transaction.id,
                                )
                              }
                              disabled={isSaving}
                            >
                              Sim, criar regra
                            </button>
                            <button
                              type="button"
                              className={styles.button}
                              onClick={() => void saveCategory(transaction, false)}
                              disabled={isSaving}
                            >
                              So guardar categoria
                            </button>
                            <button
                              type="button"
                              className={styles.ghostButton}
                              onClick={() => {
                                setPromptRowId(null);
                                setRuleFormRowId(null);
                              }}
                              disabled={isSaving}
                            >
                              Cancelar
                            </button>
                          </div>

                          {ruleFormRowId === transaction.id ? (
                            <div className={styles.ruleForm}>
                              <div className={styles.ruleGrid}>
                                <input
                                  className={styles.input}
                                  value={draft.pattern}
                                  onChange={(event) =>
                                    setRuleDrafts((current) => ({
                                      ...current,
                                      [transaction.id]: {
                                        ...draft,
                                        pattern: event.target.value,
                                      },
                                    }))
                                  }
                                  placeholder="Padrao da regra"
                                />
                                <select
                                  className={styles.select}
                                  value={draft.matchType}
                                  onChange={(event) =>
                                    setRuleDrafts((current) => ({
                                      ...current,
                                      [transaction.id]: {
                                        ...draft,
                                        matchType: event.target.value as MatchType,
                                      },
                                    }))
                                  }
                                >
                                  <option value="merchant_eq">merchant_eq</option>
                                  <option value="contains">contains</option>
                                  <option value="regex">regex</option>
                                </select>
                              </div>
                              <div className={styles.actions}>
                                <button
                                  type="button"
                                  className={styles.button}
                                  onClick={() => void saveCategory(transaction, true, draft)}
                                  disabled={isSaving || draft.pattern.trim().length === 0}
                                >
                                  Create Rule
                                </button>
                              </div>
                            </div>
                          ) : null}
                        </div>
                      ) : null}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}

function formatCurrency(value: number, currency: string) {
  return new Intl.NumberFormat("pt-PT", {
    style: "currency",
    currency,
    maximumFractionDigits: 2,
  }).format(value);
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat("pt-PT", {
    day: "2-digit",
    month: "short",
    year: "numeric",
  }).format(new Date(`${value}T00:00:00`));
}
