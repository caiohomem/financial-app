"use client";

import Link from "next/link";
import { useCallback, useRef, useState } from "react";
import { formatFileSize } from "../lib/format";
import { uploadImport, type ImportResult } from "../lib/api";

const ACCEPTED_EXTENSIONS = [".csv", ".pdf"];

const PageStyle = {
  container: { backgroundColor: "var(--bg-primary)" } as const,
  card: { backgroundColor: "var(--bg-secondary)", borderColor: "var(--border)" } as const,
  header: { color: "var(--text-primary)" } as const,
  secondary: { color: "var(--text-secondary)" } as const,
  tertiary: { color: "var(--text-tertiary)" } as const,
};

type FileUploadStatus = {
  name: string;
  status: "pending" | "uploading" | "success" | "error";
  result?: ImportResult;
  error?: string;
};

export default function ImportPage() {
  const inputRef = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);
  const [selectedFiles, setSelectedFiles] = useState<File[]>([]);
  const [uploading, setUploading] = useState(false);
  const [fileStatuses, setFileStatuses] = useState<FileUploadStatus[]>([]);
  const [error, setError] = useState<string | null>(null);

  const MAX_CONCURRENT_UPLOADS = 2;

  const pickFiles = useCallback((files: FileList | null) => {
    setError(null);
    setFileStatuses([]);

    if (!files || files.length === 0) {
      setSelectedFiles([]);
      return;
    }

    const validFiles: File[] = [];
    const errors: string[] = [];

    for (let i = 0; i < files.length; i++) {
      const file = files[i];
      const extension = file.name.slice(file.name.lastIndexOf(".")).toLowerCase();

      if (!ACCEPTED_EXTENSIONS.includes(extension)) {
        errors.push(`${file.name}: tipo não suportado (${extension})`);
      } else {
        validFiles.push(file);
      }
    }

    if (errors.length > 0) {
      setError(errors.join("\n"));
    }

    setSelectedFiles(validFiles);
  }, []);

  async function handleUpload() {
    if (selectedFiles.length === 0) {
      return;
    }

    try {
      setUploading(true);
      setError(null);

      // Initialize status for all files
      const initialStatuses: FileUploadStatus[] = selectedFiles.map((file) => ({
        name: file.name,
        status: "pending",
      }));
      setFileStatuses(initialStatuses);

      // Process files with concurrency limit
      const queue = [...selectedFiles];
      const inProgress = new Set<string>();

      const updateStatus = (fileName: string, status: FileUploadStatus["status"], data?: { result?: ImportResult; error?: string }) => {
        setFileStatuses((prev) =>
          prev.map((s) => (s.name === fileName ? { ...s, status, ...data } : s))
        );
      };

      const processFile = async (file: File) => {
        updateStatus(file.name, "uploading");
        try {
          const result = await uploadImport(file);
          updateStatus(file.name, "success", { result });
        } catch (err) {
          updateStatus(file.name, "error", {
            error: err instanceof Error ? err.message : "Erro desconhecido",
          });
        } finally {
          inProgress.delete(file.name);
        }
      };

      // Start initial batch of uploads
      while (inProgress.size < MAX_CONCURRENT_UPLOADS && queue.length > 0) {
        const file = queue.shift()!;
        inProgress.add(file.name);
        void processFile(file);
      }

      // Process remaining files as slots open up
      while (queue.length > 0 || inProgress.size > 0) {
        if (inProgress.size < MAX_CONCURRENT_UPLOADS && queue.length > 0) {
          const file = queue.shift()!;
          inProgress.add(file.name);
          void processFile(file);
        }
        await new Promise((resolve) => setTimeout(resolve, 100));
      }

      setSelectedFiles([]);
      if (inputRef.current) {
        inputRef.current.value = "";
      }
    } catch (uploadError) {
      setError(uploadError instanceof Error ? uploadError.message : "Falha ao importar ficheiros.");
    } finally {
      setUploading(false);
    }
  }

  return (
    <div style={PageStyle.container} className="min-h-screen">
      <div className="max-w-7xl mx-auto px-6 py-8">
        {/* Header */}
        <div className="mb-8">
          <h1 style={PageStyle.header} className="text-4xl font-bold mb-2">
            Importar Extratos
          </h1>
          <p style={PageStyle.secondary} className="text-sm">
            Carregue CSV da Wise ou PDF do ActivoBank. Os dados são parseados, deduplicados e gravados em PostgreSQL.
          </p>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
          {/* Dropzone */}
          <div className="lg:col-span-2">
            <div
              className="rounded-xl p-8 border-2 border-dashed transition-all"
              style={{
                ...PageStyle.card,
                borderColor: dragging ? "var(--accent)" : "var(--border)",
                backgroundColor: dragging ? "var(--bg-tertiary)" : "var(--bg-secondary)",
              }}
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
                pickFiles(event.dataTransfer.files);
              }}
            >
              <input
                ref={inputRef}
                id="import-file"
                type="file"
                accept=".csv,.pdf"
                multiple
                onChange={(event) => pickFiles(event.target.files)}
                className="hidden"
              />
              <label htmlFor="import-file" className="cursor-pointer block text-center">
                <div style={{ color: "var(--accent)" }} className="text-4xl mb-3">
                  ↑
                </div>
                <h2 style={PageStyle.header} className="text-lg font-semibold mb-1">
                  {selectedFiles.length > 0
                    ? `${selectedFiles.length} ficheiro${selectedFiles.length !== 1 ? "s" : ""} selecionado${selectedFiles.length !== 1 ? "s" : ""}`
                    : "Arrastar ficheiros para aqui"}
                </h2>
                <p style={PageStyle.tertiary} className="text-sm">
                  {selectedFiles.length > 0
                    ? selectedFiles.reduce((sum, f) => sum + f.size, 0) > 0
                      ? formatFileSize(selectedFiles.reduce((sum, f) => sum + f.size, 0))
                      : "0 B"
                    : "Ou clique para selecionar · CSV ou PDF"}
                </p>
              </label>

              {selectedFiles.length > 0 && (
                <div className="mt-4 space-y-2">
                  {selectedFiles.map((file) => (
                    <div
                      key={file.name}
                      className="flex items-center justify-between p-3 rounded-lg"
                      style={{ backgroundColor: "var(--bg-tertiary)" }}
                    >
                      <div className="flex-1">
                        <p style={{ color: "var(--text-primary)" }} className="text-sm font-medium">
                          {file.name}
                        </p>
                        <p style={{ color: "var(--text-tertiary)" }} className="text-xs">
                          {formatFileSize(file.size)}
                        </p>
                      </div>
                      {!uploading && (
                        <button
                          onClick={() => {
                            setSelectedFiles((prev) => prev.filter((f) => f.name !== file.name));
                          }}
                          className="text-sm font-medium"
                          style={{ color: "var(--error)" }}
                        >
                          ✕
                        </button>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </div>

            {error && (
              <div
                className="mt-4 p-4 rounded-lg border"
                style={{
                  backgroundColor: "rgba(239, 68, 68, 0.1)",
                  borderColor: "var(--error)",
                  color: "var(--error)",
                }}
              >
                {error}
              </div>
            )}

            <div className="flex gap-3 mt-6">
              <button
                onClick={() => void handleUpload()}
                disabled={selectedFiles.length === 0 || uploading}
                className="flex-1 px-6 py-3 rounded-lg font-medium transition-all disabled:opacity-50"
                style={{
                  backgroundColor: "var(--accent)",
                  color: "white",
                }}
              >
                {uploading ? "A importar…" : `Importar (${selectedFiles.length})`}
              </button>
              {selectedFiles.length > 0 && (
                <button
                  onClick={() => pickFiles(null)}
                  disabled={uploading}
                  className="px-6 py-3 rounded-lg font-medium transition-all"
                  style={{
                    backgroundColor: "var(--bg-tertiary)",
                    color: "var(--text-secondary)",
                    borderColor: "var(--border)",
                    borderWidth: "1px",
                  }}
                >
                  Limpar Tudo
                </button>
              )}
            </div>
          </div>

          {/* Sidebar */}
          <div className="space-y-6">
            {/* Formatos */}
            <div className="rounded-xl p-6 border" style={PageStyle.card}>
              <h3 style={PageStyle.header} className="text-sm font-semibold mb-4">
                Formatos Suportados
              </h3>
              <div className="space-y-3">
                <div>
                  <span
                    className="inline-block px-2 py-1 rounded text-xs font-medium mb-2"
                    style={{
                      backgroundColor: "rgba(59, 130, 246, 0.1)",
                      color: "var(--accent)",
                    }}
                  >
                    CSV
                  </span>
                  <p style={PageStyle.header} className="text-sm font-medium">
                    Wise
                  </p>
                  <p style={PageStyle.tertiary} className="text-xs">
                    Exportação de transferências
                  </p>
                </div>
                <div>
                  <span
                    className="inline-block px-2 py-1 rounded text-xs font-medium mb-2"
                    style={{
                      backgroundColor: "rgba(59, 130, 246, 0.1)",
                      color: "var(--accent)",
                    }}
                  >
                    PDF
                  </span>
                  <p style={PageStyle.header} className="text-sm font-medium">
                    ActivoBank
                  </p>
                  <p style={PageStyle.tertiary} className="text-xs">
                    Extrato mensal em PDF
                  </p>
                </div>
              </div>
            </div>

            {/* Resultado */}
            {fileStatuses.length > 0 ? (
              <div className="rounded-xl p-6 border" style={PageStyle.card}>
                <h3 style={PageStyle.header} className="text-sm font-semibold mb-4">
                  Progresso da Importação
                </h3>
                <div className="space-y-2 mb-4 max-h-96 overflow-y-auto">
                  {fileStatuses.map((status) => (
                    <div
                      key={status.name}
                      className="flex items-center justify-between p-2 rounded text-xs"
                      style={{
                        backgroundColor: "var(--bg-tertiary)",
                      }}
                    >
                      <div className="flex-1">
                        <p style={{ color: "var(--text-primary)" }} className="font-medium truncate">
                          {status.name}
                        </p>
                        {status.status === "success" && status.result && (
                          <p style={{ color: "var(--text-tertiary)" }}>
                            {status.result.imported} importadas, {status.result.ignored} duplicadas
                          </p>
                        )}
                        {status.status === "error" && (
                          <p style={{ color: "var(--error)" }}>
                            {status.error}
                          </p>
                        )}
                      </div>
                      <div>
                        {status.status === "pending" && (
                          <span style={{ color: "var(--text-tertiary)" }}>⏳</span>
                        )}
                        {status.status === "uploading" && (
                          <span style={{ color: "var(--accent)" }}>⟳</span>
                        )}
                        {status.status === "success" && (
                          <span style={{ color: "var(--success)" }}>✓</span>
                        )}
                        {status.status === "error" && (
                          <span style={{ color: "var(--error)" }}>✕</span>
                        )}
                      </div>
                    </div>
                  ))}
                </div>
                {!uploading && (
                  <Link
                    href="/"
                    className="block px-4 py-2 rounded-lg text-center font-medium transition-all"
                    style={{
                      backgroundColor: "var(--bg-tertiary)",
                      color: "var(--accent)",
                    }}
                  >
                    Ver no dashboard →
                  </Link>
                )}
              </div>
            ) : (
              <div className="rounded-xl p-6 border" style={PageStyle.card}>
                <h3 style={PageStyle.header} className="text-sm font-semibold mb-3">
                  Próximos Passos
                </h3>
                <ol style={PageStyle.tertiary} className="text-xs space-y-2">
                  <li>1. Parser extrai transações</li>
                  <li>2. Deduplicação por hash</li>
                  <li>3. Categorização (Regras + IA)</li>
                  <li>4. Dashboard atualizado</li>
                </ol>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
