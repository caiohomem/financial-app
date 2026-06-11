"use client";

import { useState } from "react";
import { resetDatabase } from "../lib/api";
import ui from "../styles/ui.module.css";
import styles from "./debug.module.css";

type ResetState = "idle" | "confirm" | "loading" | "done" | "error";

export default function DebugPage() {
  const [state, setState] = useState<ResetState>("idle");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  async function handleReset() {
    try {
      setState("loading");
      setErrorMessage(null);
      await resetDatabase();
      setState("done");
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : "Erro desconhecido.");
      setState("error");
    }
  }

  return (
    <div style={{ backgroundColor: "var(--bg-primary)" }} className="min-h-screen">
      <div className="max-w-7xl mx-auto px-6 py-8">
        <header className={ui.hero}>
          <p className={ui.eyebrow}>Financial App · debug</p>
          <div className={ui.titleRow}>
            <div>
              <h1 className={ui.title}>Debug</h1>
              <p className={ui.subtitle}>
                Utilitários para desenvolvimento e testes. Não usar em produção.
              </p>
            </div>
          </div>
        </header>

        <div className={styles.grid}>
          <section className={`${ui.panel} ${styles.dangerPanel}`}>
            <div className={ui.panelBody}>
              <div className={ui.sectionHeader}>
                <div>
                  <h2 className={ui.sectionTitle}>Zona de perigo</h2>
                  <p className={ui.sectionDescription}>
                    Operações destrutivas e irreversíveis.
                  </p>
                </div>
                <span className={`${ui.badge} ${styles.badgeDanger}`}>Irreversível</span>
              </div>

              <div className={styles.actionRow}>
                <div className={styles.actionInfo}>
                  <p className={styles.actionTitle}>Zerar base de dados</p>
                  <p className={styles.actionDescription}>
                    Apaga todas as transações, contas e batches de importação.
                    Categorias e regras de categorização são preservadas.
                  </p>
                </div>

                {state === "idle" && (
                  <button
                    type="button"
                    className={styles.dangerButton}
                    onClick={() => setState("confirm")}
                  >
                    Zerar dados
                  </button>
                )}

                {state === "confirm" && (
                  <div className={styles.confirmBox}>
                    <p className={styles.confirmText}>
                      Tens a certeza? Esta ação não pode ser desfeita.
                    </p>
                    <div className={styles.confirmActions}>
                      <button
                        type="button"
                        className={styles.dangerButton}
                        onClick={() => void handleReset()}
                      >
                        Sim, apagar tudo
                      </button>
                      <button
                        type="button"
                        className={ui.secondaryButton}
                        onClick={() => setState("idle")}
                      >
                        Cancelar
                      </button>
                    </div>
                  </div>
                )}

                {state === "loading" && (
                  <span className={styles.statusText}>A apagar…</span>
                )}

                {state === "done" && (
                  <div className={styles.successBox}>
                    <span className={`${ui.badge} ${ui.badgeSuccess}`}>Concluído</span>
                    <p className={styles.statusText}>Base de dados limpa.</p>
                    <button
                      type="button"
                      className={ui.secondaryButton}
                      onClick={() => setState("idle")}
                    >
                      Fechar
                    </button>
                  </div>
                )}

                {state === "error" && (
                  <div className={styles.errorBox}>
                    <p className={styles.errorText}>{errorMessage}</p>
                    <button
                      type="button"
                      className={ui.secondaryButton}
                      onClick={() => setState("idle")}
                    >
                      Tentar novamente
                    </button>
                  </div>
                )}
              </div>
            </div>
          </section>
        </div>
      </div>
    </div>
  );
}
