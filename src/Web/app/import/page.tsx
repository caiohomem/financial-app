"use client";

import Link from "next/link";
import { useCallback, useRef, useState } from "react";
import { formatFileSize } from "../lib/format";
import { uploadImport, type ImportResult } from "../lib/api";
import ui from "../styles/ui.module.css";
import styles from "./import.module.css";

const ACCEPTED_EXTENSIONS = [".csv", ".pdf"];

export default function ImportPage() {
  const inputRef = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<ImportResult | null>(null);

  const pickFile = useCallback((file: File | null) => {
    setError(null);
    setResult(null);

    if (!file) {
      setSelectedFile(null);
      return;
    }

    const extension = file.name.slice(file.name.lastIndexOf(".")).toLowerCase();
    if (!ACCEPTED_EXTENSIONS.includes(extension)) {
      setSelectedFile(null);
      setError(`Tipo não suportado (${extension}). Use ficheiros .csv (Wise) ou .pdf (ActivoBank).`);
      return;
    }

    setSelectedFile(file);
  }, []);

  async function handleUpload() {
    if (!selectedFile) {
      return;
    }

    try {
      setUploading(true);
      setError(null);
      const response = await uploadImport(selectedFile);
      setResult(response);
      setSelectedFile(null);
      if (inputRef.current) {
        inputRef.current.value = "";
      }
    } catch (uploadError) {
      setError(uploadError instanceof Error ? uploadError.message : "Falha ao importar ficheiro.");
    } finally {
      setUploading(false);
    }
  }

  return (
    <main className={ui.page}>
      <div className={ui.shell}>
        <header className={ui.hero}>
          <p className={ui.eyebrow}>Financial App · importação</p>
          <div className={ui.titleRow}>
            <div>
              <h1 className={ui.title}>Carregar extratos.</h1>
              <p className={ui.subtitle}>
                Importe CSV da Wise ou PDF do ActivoBank. Os dados são parseados, deduplicados
                e gravados na base de dados PostgreSQL — o dashboard reflete imediatamente o
                resultado.
              </p>
            </div>
          </div>
        </header>

        <p className={ui.dataSourceNote} role="status">
          <span aria-hidden="true">●</span>
          Dados em tempo real via API <code>/api/imports</code> — nada é simulado nesta página.
        </p>

        <div className={styles.grid}>
          <section className={ui.panel}>
            <div className={ui.panelBody}>
              <div className={ui.sectionHeader}>
                <div>
                  <h2 className={ui.sectionTitle}>Enviar ficheiro</h2>
                  <p className={ui.sectionDescription}>
                    Arraste o ficheiro ou selecione manualmente. Um ficheiro de cada vez.
                  </p>
                </div>
              </div>

              <div
                className={`${styles.dropzone} ${dragging ? styles.dropzoneActive : ""}`}
                onDragEnter={(event) => {
                  event.preventDefault();
                  setDragging(true);
                }}
                onDragOver={(event) => {
                  event.preventDefault();
                  setDragging(true);
                }}
                onDragLeave={(event) => {
                  event.preventDefault();
                  setDragging(false);
                }}
                onDrop={(event) => {
                  event.preventDefault();
                  setDragging(false);
                  pickFile(event.dataTransfer.files[0] ?? null);
                }}
              >
                <input
                  ref={inputRef}
                  id="import-file"
                  className={styles.fileInput}
                  type="file"
                  accept=".csv,.pdf"
                  onChange={(event) => pickFile(event.target.files?.[0] ?? null)}
                />
                <label htmlFor="import-file" className={styles.dropzoneLabel}>
                  <span className={styles.dropzoneIcon} aria-hidden="true">
                    ↑
                  </span>
                  <span className={styles.dropzoneTitle}>
                    {selectedFile ? selectedFile.name : "Arrastar CSV ou PDF para aqui"}
                  </span>
                  <span className={styles.dropzoneHint}>
                    {selectedFile
                      ? formatFileSize(selectedFile.size)
                      : "Clique para escolher · Wise (.csv) · ActivoBank (.pdf)"}
                  </span>
                </label>
              </div>

              {error ? <p className={styles.errorMessage}>{error}</p> : null}

              <div className={styles.actions}>
                <button
                  type="button"
                  className={ui.primaryButton}
                  disabled={!selectedFile || uploading}
                  onClick={() => void handleUpload()}
                >
                  {uploading ? "A importar…" : "Importar para a base de dados"}
                </button>
                {selectedFile ? (
                  <button
                    type="button"
                    className={ui.secondaryButton}
                    disabled={uploading}
                    onClick={() => pickFile(null)}
                  >
                    Limpar seleção
                  </button>
                ) : null}
              </div>
            </div>
          </section>

          <aside className={styles.sideColumn}>
            <section className={ui.panel}>
              <div className={ui.panelBody}>
                <div className={ui.sectionHeader}>
                  <div>
                    <h2 className={ui.sectionTitle}>Formatos suportados</h2>
                    <p className={ui.sectionDescription}>Deteção automática pela extensão.</p>
                  </div>
                </div>

                <ul className={styles.formatList}>
                  <li className={styles.formatItem}>
                    <span className={`${ui.badge} ${ui.badgeNeutral}`}>CSV</span>
                    <div>
                      <p className={styles.formatName}>Wise</p>
                      <p className={styles.formatMeta}>Exportação de transferências da Wise.</p>
                    </div>
                  </li>
                  <li className={styles.formatItem}>
                    <span className={`${ui.badge} ${ui.badgeNeutral}`}>PDF</span>
                    <div>
                      <p className={styles.formatName}>ActivoBank</p>
                      <p className={styles.formatMeta}>Extrato mensal em PDF do ActivoBank.</p>
                    </div>
                  </li>
                </ul>
              </div>
            </section>

            {result ? (
              <section className={`${ui.panel} ${styles.resultPanel}`}>
                <div className={ui.panelBody}>
                  <div className={ui.sectionHeader}>
                    <div>
                      <h2 className={ui.sectionTitle}>Importação concluída</h2>
                      <p className={ui.sectionDescription}>
                        Batch #{result.batchId} · origem {result.source}
                      </p>
                    </div>
                    <span className={`${ui.badge} ${ui.badgeSuccess}`}>Sucesso</span>
                  </div>

                  <dl className={styles.resultGrid}>
                    <div>
                      <dt>Importadas</dt>
                      <dd>{result.imported}</dd>
                    </div>
                    <div>
                      <dt>Ignoradas (duplicadas)</dt>
                      <dd>{result.ignored}</dd>
                    </div>
                    <div>
                      <dt>Conta</dt>
                      <dd>#{result.accountId}</dd>
                    </div>
                  </dl>

                  {Object.keys(result.byStatus).length > 0 ? (
                    <ul className={styles.statusList}>
                      {Object.entries(result.byStatus).map(([status, count]) => (
                        <li key={status}>
                          <span>{status}</span>
                          <span>{count}</span>
                        </li>
                      ))}
                    </ul>
                  ) : null}

                  <Link href="/" className={styles.dashboardLink}>
                    Ver no dashboard →
                  </Link>
                </div>
              </section>
            ) : (
              <section className={ui.panel}>
                <div className={ui.panelBody}>
                  <h2 className={ui.sectionTitle}>O que acontece depois</h2>
                  <ol className={styles.stepsList}>
                    <li>O parser extrai transações do ficheiro.</li>
                    <li>Duplicados são ignorados via hash de deduplicação.</li>
                    <li>Regras e LLM categorizam novas linhas quando aplicável.</li>
                    <li>Dashboard, relatório e revisão passam a mostrar os dados.</li>
                  </ol>
                </div>
              </section>
            )}
          </aside>
        </div>
      </div>
    </main>
  );
}
