"use client";

import { useState } from "react";
import { approveRuleSuggestion, rejectRuleSuggestion, type RuleSuggestion } from "../lib/api";
import styles from "../review/review.module.css";

type RuleSuggestionsListProps = {
  suggestions: RuleSuggestion[];
  onResolved: (suggestionId: number) => void;
  onToast: (message: string) => void;
};

export function RuleSuggestionsList({
  suggestions,
  onResolved,
  onToast,
}: RuleSuggestionsListProps) {
  const [confirming, setConfirming] = useState<Record<number, "approve" | "reject" | null>>({});
  const [savingId, setSavingId] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function confirmAction(suggestion: RuleSuggestion, action: "approve" | "reject") {
    try {
      setError(null);
      setSavingId(suggestion.id);

      if (action === "approve") {
        await approveRuleSuggestion(suggestion.id);
        onToast("Sugestao aprovada e promovida a regra.");
      } else {
        await rejectRuleSuggestion(suggestion.id);
        onToast("Sugestao rejeitada.");
      }

      onResolved(suggestion.id);
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Falha ao atualizar sugestao.");
    } finally {
      setSavingId(null);
      setConfirming((current) => ({ ...current, [suggestion.id]: null }));
    }
  }

  return (
    <>
      {error ? <div className={styles.errorBox}>{error}</div> : null}

      {suggestions.length === 0 ? (
        <div className={styles.emptyState}>Nothing to review.</div>
      ) : (
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th>Merchant</th>
                <th>Pattern</th>
                <th>Match type</th>
                <th>Categoria</th>
                <th>Confidence</th>
                <th>Acao</th>
              </tr>
            </thead>
            <tbody>
              {suggestions.map((suggestion) => {
                const isSaving = savingId === suggestion.id;
                const confirmation = confirming[suggestion.id];
                const merchantLabel = suggestion.normalizedMerchant ?? `Transacao #${suggestion.transactionId}`;
                const confidenceClass =
                  suggestion.confidence >= 0.85 ? styles.successBadge : styles.warningBadge;

                return (
                  <tr key={suggestion.id}>
                    <td>
                      <div className={styles.merchantCell}>
                        <span className={styles.merchantName}>{merchantLabel}</span>
                        <span className={styles.merchantMeta}>
                          Transacao #{suggestion.transactionId}
                        </span>
                      </div>
                    </td>
                    <td className={styles.merchantMeta}>{suggestion.suggestedPattern}</td>
                    <td>{suggestion.suggestedMatchType}</td>
                    <td>{suggestion.categoryName}</td>
                    <td>
                      <span className={confidenceClass}>{formatConfidence(suggestion.confidence)}</span>
                    </td>
                    <td>
                      {confirmation ? (
                        <div className={styles.inlinePrompt}>
                          <p className={styles.promptText}>
                            {confirmation === "approve"
                              ? "Tem a certeza que quer aprovar esta regra?"
                              : "Tem a certeza que quer rejeitar esta sugestao?"}
                          </p>
                          <div className={styles.actions}>
                            <button
                              type="button"
                              className={
                                confirmation === "approve"
                                  ? styles.button
                                  : styles.dangerButton
                              }
                              onClick={() => void confirmAction(suggestion, confirmation)}
                              disabled={isSaving}
                            >
                              Confirmar
                            </button>
                            <button
                              type="button"
                              className={styles.ghostButton}
                              onClick={() =>
                                setConfirming((current) => ({ ...current, [suggestion.id]: null }))
                              }
                              disabled={isSaving}
                            >
                              Cancelar
                            </button>
                          </div>
                        </div>
                      ) : (
                        <div className={styles.actions}>
                          <button
                            type="button"
                            className={styles.button}
                            onClick={() =>
                              setConfirming((current) => ({ ...current, [suggestion.id]: "approve" }))
                            }
                            disabled={isSaving}
                          >
                            Approve
                          </button>
                          <button
                            type="button"
                            className={styles.dangerButton}
                            onClick={() =>
                              setConfirming((current) => ({ ...current, [suggestion.id]: "reject" }))
                            }
                            disabled={isSaving}
                          >
                            Reject
                          </button>
                        </div>
                      )}
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

function formatConfidence(value: number) {
  return `${Math.round(value * 100)}%`;
}
