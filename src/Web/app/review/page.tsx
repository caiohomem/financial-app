"use client";

import { useEffect, useState } from "react";
import { PendingTransactionsList } from "../components/PendingTransactionsList";
import { RuleSuggestionsList } from "../components/RuleSuggestionsList";
import {
  getCategories,
  getReviewTransactions,
  getRuleSuggestions,
  type Category,
  type ReviewTransaction,
  type RuleSuggestion,
} from "../lib/api";
import styles from "./review.module.css";

export default function ReviewPage() {
  const [transactions, setTransactions] = useState<ReviewTransaction[]>([]);
  const [suggestions, setSuggestions] = useState<RuleSuggestion[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [toast, setToast] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function loadReviewData() {
      try {
        setLoading(true);
        setError(null);

        const [transactionsResponse, suggestionsResponse, categoriesResponse] = await Promise.all([
          getReviewTransactions(),
          getRuleSuggestions(),
          getCategories(),
        ]);

        if (cancelled) {
          return;
        }

        setTransactions(transactionsResponse);
        setSuggestions(suggestionsResponse);
        setCategories(categoriesResponse);
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
      <main className={styles.loading}>
        <div className={styles.loadingCard}>
          <h1 className={styles.sectionTitle}>A carregar fila de revisao</h1>
          <p className={styles.sectionDescription}>
            A preparar transacoes pendentes, categorias canonicas e sugestoes de regra.
          </p>
        </div>
      </main>
    );
  }

  if (error) {
    return (
      <main className={styles.error}>
        <div className={styles.errorCard}>
          <h1 className={styles.sectionTitle}>Falha ao carregar revisao</h1>
          <p className={styles.sectionDescription}>{error}</p>
        </div>
      </main>
    );
  }

  return (
    <main className={styles.page}>
      <div className={styles.shell}>
        <header className={styles.hero}>
          <p className={styles.eyebrow}>Financial App · review loop</p>
          <div className={styles.titleRow}>
            <div>
              <h1 className={styles.title}>Revisao que ensina o sistema.</h1>
              <p className={styles.subtitle}>
                Corrija categorias com uma acao deliberada, decida se a correcao deve virar
                regra por merchant e trate as sugestoes pendentes do agente antes que a fila
                cresca.
              </p>
            </div>
            <span className={styles.heroBadge}>
              {transactions.length + suggestions.length} itens em fila
            </span>
          </div>
        </header>

        <div className={styles.stack}>
          <section className={styles.panel}>
            <div className={styles.panelBody}>
              <div className={styles.sectionHeader}>
                <div>
                  <h2 className={styles.sectionTitle}>Transactions to Review</h2>
                  <p className={styles.sectionDescription}>
                    Transacoes sem categoria ou ligadas a sugestoes pendentes do agente.
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
                onToast={setToast}
              />
            </div>
          </section>

          <section className={styles.panel}>
            <div className={styles.panelBody}>
              <div className={styles.sectionHeader}>
                <div>
                  <h2 className={styles.sectionTitle}>Rule Suggestions</h2>
                  <p className={styles.sectionDescription}>
                    Regras sugeridas pelo agente que ainda precisam de confirmacao humana.
                  </p>
                </div>
                <div className={styles.badgeRow}>
                  <span className={styles.softBadge}>{suggestions.length} pendentes</span>
                </div>
              </div>

              <RuleSuggestionsList
                suggestions={suggestions}
                onResolved={(suggestionId) =>
                  setSuggestions((current) =>
                    current.filter((suggestion) => suggestion.id !== suggestionId),
                  )
                }
                onToast={setToast}
              />
            </div>
          </section>
        </div>
      </div>
    </main>
  );
}
