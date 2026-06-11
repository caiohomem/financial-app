"use client";

import { useEffect, useState } from "react";
import { PendingTransactionsList } from "../components/PendingTransactionsList";
import { RuleSuggestionsList } from "../components/RuleSuggestionsList";
import {
  getCategories,
  getReviewTransactions,
  getRuleSuggestions,
  getTransferCandidates,
  markTransfers,
  type Category,
  type ReviewTransaction,
  type RuleSuggestion,
  type TransferCandidate,
} from "../lib/api";
import styles from "./review.module.css";

export default function ReviewPage() {
  const [transactions, setTransactions] = useState<ReviewTransaction[]>([]);
  const [suggestions, setSuggestions] = useState<RuleSuggestion[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [transferCandidates, setTransferCandidates] = useState<TransferCandidate[]>([]);
  const [toast, setToast] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [markingTransfer, setMarkingTransfer] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function loadReviewData() {
      try {
        setLoading(true);
        setError(null);

        const [transactionsResponse, suggestionsResponse, categoriesResponse, transfersResponse] = await Promise.all([
          getReviewTransactions(),
          getRuleSuggestions(),
          getCategories(),
          getTransferCandidates(),
        ]);

        if (cancelled) {
          return;
        }

        setTransactions(transactionsResponse);
        setSuggestions(suggestionsResponse);
        setCategories(categoriesResponse);
        setTransferCandidates(transfersResponse);
      } catch (loadError) {
        if (!cancelled) {
          setError(loadError instanceof Error ? loadError.message : "Failed to load review page.");
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    void loadReviewData();

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (!toast) {
      return;
    }

    const timeout = window.setTimeout(() => setToast(null), 3200);
    return () => window.clearTimeout(timeout);
  }, [toast]);

  if (loading) {
    return (
      <div style={{ backgroundColor: "var(--bg-primary)" }} className="min-h-screen flex flex-col items-center justify-center px-4">
        <div className="rounded-xl p-8 border text-center" style={{ backgroundColor: "var(--bg-secondary)", borderColor: "var(--border)" }}>
          <div className="mb-4 inline-block">
            <div className="w-12 h-12 rounded-lg animate-spin" style={{ borderTop: "3px solid var(--accent)", borderRight: "3px solid transparent" }} />
          </div>
          <h1 style={{ color: "var(--text-primary)", marginTop: 0 }} className="text-lg font-semibold">
            A carregar fila de revisão
          </h1>
          <p style={{ color: "var(--text-tertiary)" }} className="text-sm">
            A preparar transações pendentes, categorias e sugestões de regra.
          </p>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div style={{ backgroundColor: "var(--bg-primary)" }} className="min-h-screen flex flex-col items-center justify-center px-4">
        <div className="rounded-xl p-8 border text-center max-w-md" style={{ backgroundColor: "var(--bg-secondary)", borderColor: "var(--border)" }}>
          <h1 style={{ color: "var(--error)", marginTop: 0 }} className="text-lg font-semibold">
            Falha ao carregar revisão
          </h1>
          <p style={{ color: "var(--text-secondary)" }} className="text-sm">
            {error}
          </p>
        </div>
      </div>
    );
  }

  return (
    <div style={{ backgroundColor: "var(--bg-primary)" }} className="min-h-screen">
      <div className="max-w-7xl mx-auto px-6 py-8">
        <header className={styles.hero}>
          <p className={styles.eyebrow}>Financial App · review loop</p>
          <div className={styles.titleRow}>
            <div>
              <h1 className={styles.title}>Revisão</h1>
              <p className={styles.subtitle}>
                Corrija categorias com uma acao deliberada, decida se a correcao deve virar
                regra por merchant e trate as sugestoes pendentes do agente antes que a fila
                cresca.
              </p>
            </div>
            <span className={styles.heroBadge}>
              {transactions.length + suggestions.length + transferCandidates.length} itens em fila
            </span>
          </div>
        </header>

        <div className={styles.stack}>
          <section className={styles.panel}>
            <div className={styles.panelBody}>
              <div className={styles.sectionHeader}>
                <div>
                  <h2 className={styles.sectionTitle}>Transações por rever</h2>
                  <p className={styles.sectionDescription}>
                    Transações sem categoria ou ligadas a sugestões pendentes do agente.
                  </p>
                </div>
                <div className={styles.badgeRow}>
                  <span className={styles.softBadge}>{transactions.length} pendentes</span>
                </div>
              </div>

              {toast ? <div className={styles.feedback}>{toast}</div> : null}

              <PendingTransactionsList
                transactions={transactions}
                categories={categories}
                onResolved={(transactionId) => {
                  setTransactions((current) =>
                    current.filter((transaction) => transaction.id !== transactionId),
                  );
                  setSuggestions((current) =>
                    current.filter((suggestion) => suggestion.transactionId !== transactionId),
                  );
                }}
                onCategoryCreated={(category) =>
                  setCategories((current) => [...current, category])
                }
                onToast={setToast}
              />
            </div>
          </section>

          <section className={styles.panel}>
            <div className={styles.panelBody}>
              <div className={styles.sectionHeader}>
                <div>
                  <h2 className={styles.sectionTitle}>Sugestões de regras</h2>
                  <p className={styles.sectionDescription}>
                    Regras sugeridas pelo agente que ainda precisam de confirmação humana.
                  </p>
                </div>
                <div className={styles.badgeRow}>
                  <span className={styles.softBadge}>{suggestions.length} pendentes</span>
                </div>
              </div>

              <RuleSuggestionsList
                suggestions={suggestions}
                categories={categories}
                onResolved={(suggestionId) =>
                  setSuggestions((current) =>
                    current.filter((suggestion) => suggestion.id !== suggestionId),
                  )
                }
                onToast={setToast}
              />
            </div>
          </section>
          {transferCandidates.length > 0 && (
            <section className={styles.panel}>
              <div className={styles.panelBody}>
                <div className={styles.sectionHeader}>
                  <div>
                    <h2 className={styles.sectionTitle}>Transferências internas</h2>
                    <p className={styles.sectionDescription}>
                      Pares de transações com o mesmo valor entre as suas contas. Confirma as que são
                      transferências próprias — serão excluídas dos cálculos de gastos.
                    </p>
                  </div>
                  <span className={styles.softBadge}>{transferCandidates.length} pares</span>
                </div>

                {transferCandidates.map((candidate) => {
                  const key = `${candidate.outId}-${candidate.inId}`;
                  const isMarking = markingTransfer === key;

                  return (
                    <div key={key} className={styles.transferCard}>
                      <div className={styles.transferInfo}>
                        <span className={styles.transferAmount}>
                          {new Intl.NumberFormat("pt-PT", { style: "currency", currency: candidate.outCurrency }).format(candidate.amount)}
                        </span>
                        <span className={styles.transferMeta}>
                          Saída: {candidate.outAccount} ({candidate.outSource}) em {candidate.outDate}
                        </span>
                        <span className={styles.transferMeta}>
                          Entrada: {candidate.inAccount} ({candidate.inSource}) em {candidate.inDate}
                        </span>
                      </div>
                      <div className={styles.actions}>
                        <button
                          type="button"
                          className={styles.button}
                          disabled={isMarking}
                          onClick={async () => {
                            setMarkingTransfer(key);
                            try {
                              const res = await markTransfers([candidate.outId, candidate.inId], true);
                              setTransferCandidates((current) =>
                                current.filter((c) => c.outId !== candidate.outId || c.inId !== candidate.inId),
                              );
                              setToast(`${res.markedTransactions} transações marcadas como transferência interna.`);
                            } catch {
                              setToast("Erro ao marcar transferência.");
                            } finally {
                              setMarkingTransfer(null);
                            }
                          }}
                        >
                          {isMarking ? "…" : "É transferência minha"}
                        </button>
                        <button
                          type="button"
                          className={styles.ghostButton}
                          disabled={isMarking}
                          onClick={() =>
                            setTransferCandidates((current) =>
                              current.filter((c) => c.outId !== candidate.outId || c.inId !== candidate.inId),
                            )
                          }
                        >
                          Ignorar
                        </button>
                      </div>
                    </div>
                  );
                })}
              </div>
            </section>
          )}
        </div>
      </div>
    </div>
  );
}
