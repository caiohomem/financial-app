"use client";

import { useState } from "react";
import {
  approveRuleSuggestion,
  rejectRuleSuggestion,
  previewRuleSuggestion,
  type Category,
  type RuleSuggestion,
  type RuleSuggestionPreview,
} from "../lib/api";
import { CategorySelectWithCreate } from "./CategorySelectWithCreate";
import styles from "../review/review.module.css";

type RuleSuggestionsListProps = {
  suggestions: RuleSuggestion[];
  categories: Category[];
  onResolved: (suggestionId: number) => void;
  onToast: (message: string) => void;
};

type MatchType = "merchant_eq" | "contains" | "regex";

type EditDraft = {
  pattern: string;
  matchType: MatchType;
  categoryId: string;
};

export function RuleSuggestionsList({
  suggestions,
  categories,
  onResolved,
  onToast,
}: RuleSuggestionsListProps) {
  const [savingId, setSavingId] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [editingId, setEditingId] = useState<number | null>(null);
  const [editDraft, setEditDraft] = useState<EditDraft>({ pattern: "", matchType: "contains", categoryId: "" });

  const [rejectingId, setRejectingId] = useState<number | null>(null);

  const [previewId, setPreviewId] = useState<number | null>(null);
  const [preview, setPreview] = useState<RuleSuggestionPreview | null>(null);
  const [previewLoading, setPreviewLoading] = useState(false);

  const handleCategoryCreated = (newCategory: Category) => {
    setEditDraft((d) => ({ ...d, categoryId: String(newCategory.id) }));
  };

  function startEdit(suggestion: RuleSuggestion) {
    setEditingId(suggestion.id);
    setEditDraft({
      pattern: suggestion.suggestedPattern,
      matchType: suggestion.suggestedMatchType,
      categoryId: String(suggestion.categoryCanonicalId),
    });
    setRejectingId(null);
    setPreviewId(null);
    setPreview(null);
  }

  async function handlePreview(suggestion: RuleSuggestion) {
    if (previewId === suggestion.id) {
      setPreviewId(null);
      setPreview(null);
      return;
    }

    try {
      setPreviewLoading(true);
      setPreviewId(suggestion.id);
      const data = await previewRuleSuggestion(suggestion.id);
      setPreview(data);
    } catch {
      setPreview(null);
    } finally {
      setPreviewLoading(false);
    }
  }

  async function handleApprove(suggestion: RuleSuggestion) {
    try {
      setError(null);
      setSavingId(suggestion.id);

      const isEditing = editingId === suggestion.id;
      const overrides = isEditing
        ? {
            pattern: editDraft.pattern.trim(),
            matchType: editDraft.matchType,
            categoryId: Number(editDraft.categoryId),
          }
        : undefined;

      const result = await approveRuleSuggestion(suggestion.id, overrides);
      onToast(`Regra aprovada — ${result.recategorizedTransactions} transações recategorizadas.`);
      onResolved(suggestion.id);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Falha ao aprovar sugestão.");
    } finally {
      setSavingId(null);
      setEditingId(null);
    }
  }

  async function handleReject(suggestion: RuleSuggestion) {
    try {
      setError(null);
      setSavingId(suggestion.id);
      await rejectRuleSuggestion(suggestion.id);
      onToast("Sugestão rejeitada.");
      onResolved(suggestion.id);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Falha ao rejeitar sugestão.");
    } finally {
      setSavingId(null);
      setRejectingId(null);
    }
  }

  return (
    <>
      {error ? <div className={styles.errorBox}>{error}</div> : null}

      {suggestions.length === 0 ? (
        <div className={styles.emptyState}>Nenhuma sugestão pendente.</div>
      ) : (
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th>Merchant</th>
                <th>Padrão</th>
                <th>Tipo</th>
                <th>Categoria</th>
                <th>Conf.</th>
                <th>Ação</th>
              </tr>
            </thead>
            <tbody>
              {suggestions.map((suggestion) => {
                const isSaving = savingId === suggestion.id;
                const isEditing = editingId === suggestion.id;
                const isRejecting = rejectingId === suggestion.id;
                const isPreviewOpen = previewId === suggestion.id;
                const merchantLabel = suggestion.normalizedMerchant ?? `Transação #${suggestion.transactionId}`;
                const confidenceClass = suggestion.confidence >= 0.85 ? styles.successBadge : styles.warningBadge;

                return (
                  <>
                    <tr key={suggestion.id}>
                      <td>
                        <div className={styles.merchantCell}>
                          <span className={styles.merchantName}>{merchantLabel}</span>
                          <span className={styles.merchantMeta}>#{suggestion.transactionId}</span>
                        </div>
                      </td>

                      <td>
                        {isEditing ? (
                          <input
                            className={styles.input}
                            value={editDraft.pattern}
                            onChange={(e) => setEditDraft((d) => ({ ...d, pattern: e.target.value }))}
                            disabled={isSaving}
                            autoFocus
                          />
                        ) : (
                          <span className={styles.merchantMeta}>{suggestion.suggestedPattern}</span>
                        )}
                      </td>

                      <td>
                        {isEditing ? (
                          <select
                            className={styles.select}
                            value={editDraft.matchType}
                            onChange={(e) => setEditDraft((d) => ({ ...d, matchType: e.target.value as MatchType }))}
                            disabled={isSaving}
                          >
                            <option value="merchant_eq">merchant_eq</option>
                            <option value="contains">contains</option>
                            <option value="regex">regex</option>
                          </select>
                        ) : (
                          suggestion.suggestedMatchType
                        )}
                      </td>

                      <td>
                        {isEditing ? (
                          <CategorySelectWithCreate
                            categories={categories}
                            selectedId={Number(editDraft.categoryId)}
                            onChange={(id) => setEditDraft((d) => ({ ...d, categoryId: String(id) }))}
                            onCategoryCreated={handleCategoryCreated}
                            disabled={isSaving}
                            className={styles.select}
                          />
                        ) : (
                          suggestion.categoryName
                        )}
                      </td>

                      <td>
                        <span className={confidenceClass}>{formatConfidence(suggestion.confidence)}</span>
                      </td>

                      <td>
                        <div className={styles.actions}>
                          {isEditing ? (
                            <>
                              <button
                                type="button"
                                className={styles.button}
                                onClick={() => void handleApprove(suggestion)}
                                disabled={isSaving || !editDraft.pattern.trim()}
                              >
                                {isSaving ? "…" : "Aprovar"}
                              </button>
                              <button
                                type="button"
                                className={styles.ghostButton}
                                onClick={() => setEditingId(null)}
                                disabled={isSaving}
                              >
                                Cancelar
                              </button>
                            </>
                          ) : isRejecting ? (
                            <>
                              <span className={styles.merchantMeta}>Rejeitar?</span>
                              <button
                                type="button"
                                className={styles.dangerButton}
                                onClick={() => void handleReject(suggestion)}
                                disabled={isSaving}
                              >
                                {isSaving ? "…" : "Sim"}
                              </button>
                              <button
                                type="button"
                                className={styles.ghostButton}
                                onClick={() => setRejectingId(null)}
                                disabled={isSaving}
                              >
                                Não
                              </button>
                            </>
                          ) : (
                            <>
                              <button
                                type="button"
                                className={styles.button}
                                onClick={() => void handleApprove(suggestion)}
                                disabled={isSaving}
                              >
                                Aprovar
                              </button>
                              <button
                                type="button"
                                className={styles.secondaryButton}
                                onClick={() => startEdit(suggestion)}
                                disabled={isSaving}
                              >
                                Editar
                              </button>
                              <button
                                type="button"
                                className={styles.ghostButton}
                                onClick={() => void handlePreview(suggestion)}
                                disabled={isSaving}
                              >
                                {isPreviewOpen ? "Fechar" : "Ver transações"}
                              </button>
                              <button
                                type="button"
                                className={styles.dangerButton}
                                onClick={() => { setRejectingId(suggestion.id); setEditingId(null); }}
                                disabled={isSaving}
                              >
                                Rejeitar
                              </button>
                            </>
                          )}
                        </div>
                      </td>
                    </tr>

                    {isPreviewOpen && (
                      <tr key={`preview-${suggestion.id}`}>
                        <td colSpan={6} className={styles.previewCell}>
                          {previewLoading ? (
                            <p className={styles.merchantMeta}>A carregar transações…</p>
                          ) : preview && preview.matches.length > 0 ? (
                            <div className={styles.previewBox}>
                              <p className={styles.promptTitle}>
                                {preview.matches.length} transação(ões) que correspondem ao padrão &ldquo;{preview.pattern}&rdquo;
                              </p>
                              <table className={styles.table}>
                                <thead>
                                  <tr>
                                    <th>Data</th>
                                    <th>Merchant / Descrição</th>
                                    <th>Montante</th>
                                  </tr>
                                </thead>
                                <tbody>
                                  {preview.matches.map((match) => (
                                    <tr key={match.id} className={match.isOrigin ? styles.previewOriginRow : undefined}>
                                      <td className={styles.merchantMeta}>{match.bookingDate}</td>
                                      <td>
                                        <div className={styles.merchantCell}>
                                          <span className={match.isOrigin ? styles.merchantName : undefined}>
                                            {match.normalizedMerchant ?? match.rawDescription}
                                            {match.isOrigin ? " ★" : ""}
                                          </span>
                                          {match.normalizedMerchant && (
                                            <span className={styles.merchantMeta}>{match.rawDescription}</span>
                                          )}
                                        </div>
                                      </td>
                                      <td className={match.direction === "OUT" ? styles.amountOut : styles.amountIn}>
                                        {formatCurrency(match.amount, match.currency)}
                                      </td>
                                    </tr>
                                  ))}
                                </tbody>
                              </table>
                            </div>
                          ) : (
                            <p className={styles.merchantMeta}>Nenhuma transação encontrada para este padrão.</p>
                          )}
                        </td>
                      </tr>
                    )}
                  </>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}

function formatConfidence(value: number) {
  return `${Math.round(value * 100)}%`;
}

function formatCurrency(value: number, currency: string) {
  return new Intl.NumberFormat("pt-PT", {
    style: "currency",
    currency,
    maximumFractionDigits: 2,
  }).format(value);
}
