"use client";

import { useState } from "react";
import { updateTransactionCategory, type Category } from "../lib/api";
import { CategorySelectWithCreate } from "./CategorySelectWithCreate";

type ModalProps = {
  transactionId: number;
  currentCategory: string;
  merchant: string;
  categories: Category[];
  onClose: () => void;
  onSuccess: () => void;
};

export function EditTransactionCategoryModal({
  transactionId,
  currentCategory,
  merchant,
  categories,
  onClose,
  onSuccess,
}: ModalProps) {
  const currentCatId = categories.find((c) => c.name === currentCategory)?.id || categories[0]?.id || 0;
  const [selectedCategoryId, setSelectedCategoryId] = useState(currentCatId);
  const [createRule, setCreateRule] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSave = async () => {
    if (!selectedCategoryId) return;

    try {
      setSaving(true);
      setError(null);

      await updateTransactionCategory(transactionId, selectedCategoryId, {
        createRule,
        pattern: createRule ? merchant : undefined,
        matchType: createRule ? "merchant_eq" : undefined,
      });

      onSuccess();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Erro ao atualizar categoria");
    } finally {
      setSaving(false);
    }
  };

  const handleCategoryCreated = (newCategory: Category) => {
    setSelectedCategoryId(newCategory.id);
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center px-4"
      style={{ backgroundColor: "rgba(0, 0, 0, 0.75)" }}
      onClick={onClose}
    >
      <div
        className="rounded-xl max-w-md w-full p-6 border"
        style={{
          backgroundColor: "var(--bg-secondary)",
          borderColor: "var(--border)",
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <h2 style={{ color: "var(--text-primary)", marginTop: 0 }} className="text-lg font-semibold mb-4">
          Editar Categoria
        </h2>

        <div className="mb-4">
          <label style={{ color: "var(--text-tertiary)" }} className="block text-sm mb-2">
            Merchant: <strong style={{ color: "var(--text-secondary)" }}>{merchant}</strong>
          </label>
          <label style={{ color: "var(--text-tertiary)" }} className="block text-sm mb-2">
            Categoria Atual: <strong style={{ color: "var(--text-secondary)" }}>{currentCategory}</strong>
          </label>
        </div>

        <div className="mb-6">
          <label style={{ color: "var(--text-secondary)" }} className="block text-sm font-medium mb-2">
            Nova Categoria
          </label>
          <CategorySelectWithCreate
            categories={categories}
            selectedId={selectedCategoryId}
            onChange={setSelectedCategoryId}
            onCategoryCreated={handleCategoryCreated}
            disabled={saving}
            className="w-full"
          />
        </div>

        <label className="flex items-center gap-3 mb-6 cursor-pointer">
          <input
            type="checkbox"
            checked={createRule}
            onChange={(e) => setCreateRule(e.target.checked)}
            disabled={saving}
            className="w-4 h-4"
          />
          <span style={{ color: "var(--text-secondary)" }} className="text-sm">
            Criar regra automática para <strong>&quot;{merchant}&quot;</strong>
          </span>
        </label>

        {error && (
          <div
            className="mb-4 p-3 rounded-lg text-sm"
            style={{
              backgroundColor: "rgba(239, 68, 68, 0.1)",
              color: "var(--error)",
              borderColor: "var(--error)",
              borderWidth: "1px",
            }}
          >
            {error}
          </div>
        )}

        <div className="flex gap-3">
          <button
            onClick={handleSave}
            disabled={saving || !selectedCategoryId}
            className="flex-1 px-4 py-2 rounded-lg font-medium transition-all disabled:opacity-50"
            style={{
              backgroundColor: "var(--accent)",
              color: "white",
            }}
          >
            {saving ? "Salvando…" : "Salvar"}
          </button>
          <button
            onClick={onClose}
            disabled={saving}
            className="flex-1 px-4 py-2 rounded-lg font-medium transition-all border"
            style={{
              backgroundColor: "var(--bg-tertiary)",
              color: "var(--text-secondary)",
              borderColor: "var(--border)",
            }}
          >
            Cancelar
          </button>
        </div>
      </div>
    </div>
  );
}
