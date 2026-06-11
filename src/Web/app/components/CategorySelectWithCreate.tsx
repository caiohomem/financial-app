"use client";

import { useState } from "react";
import { createCategory, type Category } from "../lib/api";

type Props = {
  categories: Category[];
  selectedId?: number;
  onChange: (categoryId: number) => void;
  onCategoryCreated?: (category: Category) => void;
  disabled?: boolean;
  className?: string;
  style?: React.CSSProperties;
};

const NEW_CATEGORY_SENTINEL = "__new__";

export function CategorySelectWithCreate({
  categories,
  selectedId,
  onChange,
  onCategoryCreated,
  disabled,
  className,
  style,
}: Props) {
  const [isCreating, setIsCreating] = useState(false);
  const [newCategoryName, setNewCategoryName] = useState("");
  const [creatingError, setCreatingError] = useState<string | null>(null);

  const handleSelectChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    const value = e.target.value;
    if (value === NEW_CATEGORY_SENTINEL) {
      setIsCreating(true);
      setNewCategoryName("");
    } else {
      onChange(Number(value));
    }
  };

  const handleCreateCategory = async () => {
    const trimmed = newCategoryName.trim();
    if (!trimmed) {
      setCreatingError("Nome não pode ser vazio");
      return;
    }

    try {
      setCreatingError(null);
      const newCategory = await createCategory(trimmed);
      onCategoryCreated?.(newCategory);
      onChange(newCategory.id);
      setIsCreating(false);
      setNewCategoryName("");
    } catch (err) {
      setCreatingError(err instanceof Error ? err.message : "Erro ao criar categoria");
    }
  };

  if (isCreating) {
    return (
      <div className="flex gap-2">
        <input
          type="text"
          value={newCategoryName}
          onChange={(e) => setNewCategoryName(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") void handleCreateCategory();
            if (e.key === "Escape") setIsCreating(false);
          }}
          placeholder="Nova categoria…"
          autoFocus
          className={`flex-1 px-4 py-2 rounded-lg border text-sm ${className}`}
          style={{
            backgroundColor: "var(--bg-tertiary)",
            borderColor: creatingError ? "var(--error)" : "var(--border)",
            color: "var(--text-primary)",
            ...style,
          }}
        />
        <button
          onClick={() => void handleCreateCategory()}
          className="px-3 py-2 rounded-lg text-sm font-medium transition-all"
          style={{
            backgroundColor: "var(--accent)",
            color: "white",
          }}
        >
          Criar
        </button>
        <button
          onClick={() => setIsCreating(false)}
          className="px-3 py-2 rounded-lg text-sm font-medium transition-all"
          style={{
            backgroundColor: "var(--bg-tertiary)",
            color: "var(--text-secondary)",
            border: "1px solid var(--border)",
          }}
        >
          Cancelar
        </button>
        {creatingError && (
          <div style={{ color: "var(--error)", fontSize: "12px", marginTop: "4px" }}>
            {creatingError}
          </div>
        )}
      </div>
    );
  }

  return (
    <select
      value={selectedId || ""}
      onChange={handleSelectChange}
      disabled={disabled}
      className={`px-4 py-2 rounded-lg border text-sm ${className}`}
      style={{
        backgroundColor: "var(--bg-tertiary)",
        borderColor: "var(--border)",
        color: "var(--text-primary)",
        ...style,
      }}
    >
      <option value="">Selecionar…</option>
      {categories.map((cat) => (
        <option key={cat.id} value={cat.id}>
          {cat.name}
        </option>
      ))}
      <option value={NEW_CATEGORY_SENTINEL}>+ Criar categoria</option>
    </select>
  );
}
