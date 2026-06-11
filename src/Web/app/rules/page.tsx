"use client";

import { useEffect, useState } from "react";
import {
  getCategories,
  getRules,
  createRule,
  updateRule,
  deleteRule,
  type Category,
  type Rule,
} from "../lib/api";
import { CategorySelectWithCreate } from "../components/CategorySelectWithCreate";
import styles from "./rules.module.css";

type MatchType = "merchant_eq" | "contains" | "regex";

type DraftRule = {
  pattern: string;
  matchType: MatchType;
  categoryId: string;
  priority: string;
};

const EMPTY_DRAFT: DraftRule = {
  pattern: "",
  matchType: "contains",
  categoryId: "",
  priority: "100",
};

const MATCH_TYPE_LABELS: Record<MatchType, string> = {
  merchant_eq: "merchant",
  contains: "contém",
  regex: "regex",
};

export default function RulesPage() {
  const [rules, setRules] = useState<Rule[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<string | null>(null);

  const [editingId, setEditingId] = useState<number | null>(null);
  const [editDraft, setEditDraft] = useState<DraftRule>(EMPTY_DRAFT);
  const [deletingId, setDeletingId] = useState<number | null>(null);
  const [saving, setSaving] = useState(false);

  const [showNewRow, setShowNewRow] = useState(false);
  const [newDraft, setNewDraft] = useState<DraftRule>(EMPTY_DRAFT);

  const handleCategoryCreated = (newCategory: Category) => {
    setCategories((current) => [...current, newCategory]);
  };

  useEffect(() => {
    let cancelled = false;

    async function load() {
      try {
        const [rulesRes, categoriesRes] = await Promise.all([getRules(), getCategories()]);
        if (!cancelled) {
          setRules(rulesRes);
          setCategories(categoriesRes);
        }
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : "Erro ao carregar.");
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    void load();
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    if (!toast) return;
    const t = window.setTimeout(() => setToast(null), 3200);
    return () => window.clearTimeout(t);
  }, [toast]);

  function startEdit(rule: Rule) {
    setEditingId(rule.id);
    setEditDraft({
      pattern: rule.pattern,
      matchType: rule.matchType,
      categoryId: String(rule.categoryId),
      priority: String(rule.priority),
    });
    setDeletingId(null);
  }

  function cancelEdit() {
    setEditingId(null);
    setEditDraft(EMPTY_DRAFT);
  }

  async function handleSaveEdit(rule: Rule) {
    if (!editDraft.pattern.trim() || !editDraft.categoryId) return;

    try {
      setSaving(true);
      setError(null);
      const res = await updateRule(rule.id, {
        pattern: editDraft.pattern.trim(),
        matchType: editDraft.matchType,
        categoryId: Number(editDraft.categoryId),
        priority: Number(editDraft.priority) || 100,
      });
      const catName = categories.find((c) => c.id === Number(editDraft.categoryId))?.name ?? "";
      setRules((current) =>
        current.map((r) =>
          r.id === rule.id
            ? {
                ...r,
                pattern: editDraft.pattern.trim(),
                matchType: editDraft.matchType,
                categoryId: Number(editDraft.categoryId),
                categoryName: catName,
                priority: Number(editDraft.priority) || 100,
              }
            : r,
        ),
      );
      setEditingId(null);
      setToast(`Regra atualizada — ${res.recategorizedTransactions} transações recategorizadas.`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Erro ao guardar.");
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete(id: number) {
    try {
      setSaving(true);
      setError(null);
      await deleteRule(id);
      setRules((current) => current.filter((r) => r.id !== id));
      setDeletingId(null);
      setToast("Regra eliminada.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Erro ao eliminar.");
    } finally {
      setSaving(false);
    }
  }

  async function handleCreate() {
    if (!newDraft.pattern.trim() || !newDraft.categoryId) return;

    try {
      setSaving(true);
      setError(null);
      const res = await createRule({
        pattern: newDraft.pattern.trim(),
        matchType: newDraft.matchType,
        categoryId: Number(newDraft.categoryId),
      });
      const [freshRules] = await Promise.all([getRules()]);
      setRules(freshRules);
      setShowNewRow(false);
      setNewDraft(EMPTY_DRAFT);
      setToast(`Regra criada — ${res.recategorizedTransactions} transações recategorizadas.`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Erro ao criar.");
    } finally {
      setSaving(false);
    }
  }

  if (loading) {
    return (
      <div style={{ backgroundColor: "var(--bg-primary)" }} className="min-h-screen flex items-center justify-center px-4">
        <div className="rounded-xl p-8 border text-center" style={{ backgroundColor: "var(--bg-secondary)", borderColor: "var(--border)" }}>
          <h1 style={{ color: "var(--text-primary)", marginTop: 0 }} className="text-lg font-semibold">
            A carregar regras…
          </h1>
        </div>
      </div>
    );
  }

  return (
    <div style={{ backgroundColor: "var(--bg-primary)" }} className="min-h-screen">
      <div className="max-w-7xl mx-auto px-6 py-8">
        <header className={styles.hero}>
          <p className={styles.eyebrow}>Financial App · regras</p>
          <div className={styles.titleRow}>
            <div>
              <h1 className={styles.title}>Regras</h1>
              <p className={styles.subtitle}>
                Padrões que mapeiam merchants e descrições para categorias. Aplicadas
                automaticamente em cada importação por ordem de prioridade.
              </p>
            </div>
            <span className={styles.heroBadge}>{rules.length} regras</span>
          </div>
        </header>

        <section className={styles.panel}>
          <div className={styles.panelBody}>
            <div className={styles.sectionHeader}>
              <div>
                <h2 className={styles.sectionTitle}>Todas as regras</h2>
                <p className={styles.sectionDescription}>
                  Ordenadas por prioridade — maior prioridade é aplicada primeiro.
                </p>
              </div>
              {!showNewRow && (
                <button
                  type="button"
                  className={styles.button}
                  onClick={() => { setShowNewRow(true); setEditingId(null); setDeletingId(null); }}
                >
                  + Nova regra
                </button>
              )}
            </div>

            {toast && <div className={styles.feedback}>{toast}</div>}
            {error && <div className={styles.errorBox}>{error}</div>}

            <div className={styles.tableWrap}>
              <table className={styles.table}>
                <thead>
                  <tr>
                    <th>Padrão</th>
                    <th>Tipo</th>
                    <th>Categoria</th>
                    <th className={styles.priorityCol}>Prioridade</th>
                    <th className={styles.actionsCol}>Ações</th>
                  </tr>
                </thead>
                <tbody>
                  {showNewRow && (
                    <tr className={styles.editRow}>
                      <td>
                        <input
                          className={styles.input}
                          value={newDraft.pattern}
                          onChange={(e) => setNewDraft((d) => ({ ...d, pattern: e.target.value }))}
                          onKeyDown={(e) => { if (e.key === "Enter") void handleCreate(); if (e.key === "Escape") { setShowNewRow(false); setNewDraft(EMPTY_DRAFT); }}}
                          placeholder="ex: Uber, Glovo, .*netflix.*"
                          autoFocus
                          disabled={saving}
                        />
                      </td>
                      <td>
                        <select
                          className={styles.select}
                          value={newDraft.matchType}
                          onChange={(e) => setNewDraft((d) => ({ ...d, matchType: e.target.value as MatchType }))}
                          disabled={saving}
                        >
                          <option value="merchant_eq">merchant_eq</option>
                          <option value="contains">contains</option>
                          <option value="regex">regex</option>
                        </select>
                      </td>
                      <td>
                        <CategorySelectWithCreate
                          categories={categories}
                          selectedId={newDraft.categoryId ? Number(newDraft.categoryId) : undefined}
                          onChange={(id) => setNewDraft((d) => ({ ...d, categoryId: String(id) }))}
                          onCategoryCreated={handleCategoryCreated}
                          disabled={saving}
                          className={styles.select}
                        />
                      </td>
                      <td>
                        <input
                          className={`${styles.input} ${styles.priorityInput}`}
                          type="number"
                          value={newDraft.priority}
                          onChange={(e) => setNewDraft((d) => ({ ...d, priority: e.target.value }))}
                          disabled={saving}
                        />
                      </td>
                      <td>
                        <div className={styles.rowActions}>
                          <button
                            type="button"
                            className={styles.button}
                            onClick={() => void handleCreate()}
                            disabled={saving || !newDraft.pattern.trim() || !newDraft.categoryId}
                          >
                            {saving ? "…" : "Criar"}
                          </button>
                          <button
                            type="button"
                            className={styles.ghostButton}
                            onClick={() => { setShowNewRow(false); setNewDraft(EMPTY_DRAFT); }}
                            disabled={saving}
                          >
                            Cancelar
                          </button>
                        </div>
                      </td>
                    </tr>
                  )}

                  {rules.length === 0 && !showNewRow && (
                    <tr>
                      <td colSpan={5}>
                        <div className={styles.emptyState}>Nenhuma regra criada ainda.</div>
                      </td>
                    </tr>
                  )}

                  {rules.map((rule) => {
                    const isEditing = editingId === rule.id;
                    const isDeleting = deletingId === rule.id;

                    if (isEditing) {
                      return (
                        <tr key={rule.id} className={styles.editRow}>
                          <td>
                            <input
                              className={styles.input}
                              value={editDraft.pattern}
                              onChange={(e) => setEditDraft((d) => ({ ...d, pattern: e.target.value }))}
                              onKeyDown={(e) => { if (e.key === "Enter") void handleSaveEdit(rule); if (e.key === "Escape") cancelEdit(); }}
                              autoFocus
                              disabled={saving}
                            />
                          </td>
                          <td>
                            <select
                              className={styles.select}
                              value={editDraft.matchType}
                              onChange={(e) => setEditDraft((d) => ({ ...d, matchType: e.target.value as MatchType }))}
                              disabled={saving}
                            >
                              <option value="merchant_eq">merchant_eq</option>
                              <option value="contains">contains</option>
                              <option value="regex">regex</option>
                            </select>
                          </td>
                          <td>
                            <CategorySelectWithCreate
                              categories={categories}
                              selectedId={editDraft.categoryId ? Number(editDraft.categoryId) : undefined}
                              onChange={(id) => setEditDraft((d) => ({ ...d, categoryId: String(id) }))}
                              onCategoryCreated={handleCategoryCreated}
                              disabled={saving}
                              className={styles.select}
                            />
                          </td>
                          <td>
                            <input
                              className={`${styles.input} ${styles.priorityInput}`}
                              type="number"
                              value={editDraft.priority}
                              onChange={(e) => setEditDraft((d) => ({ ...d, priority: e.target.value }))}
                              disabled={saving}
                            />
                          </td>
                          <td>
                            <div className={styles.rowActions}>
                              <button
                                type="button"
                                className={styles.button}
                                onClick={() => void handleSaveEdit(rule)}
                                disabled={saving || !editDraft.pattern.trim()}
                              >
                                {saving ? "…" : "Guardar"}
                              </button>
                              <button
                                type="button"
                                className={styles.ghostButton}
                                onClick={cancelEdit}
                                disabled={saving}
                              >
                                Cancelar
                              </button>
                            </div>
                          </td>
                        </tr>
                      );
                    }

                    return (
                      <tr key={rule.id} className={isDeleting ? styles.deletingRow : undefined}>
                        <td className={styles.patternCell}>{rule.pattern}</td>
                        <td>
                          <span className={`${styles.typeBadge} ${styles[`type_${rule.matchType}`]}`}>
                            {MATCH_TYPE_LABELS[rule.matchType]}
                          </span>
                        </td>
                        <td>{rule.categoryName}</td>
                        <td className={styles.priorityCell}>{rule.priority}</td>
                        <td>
                          {isDeleting ? (
                            <div className={styles.rowActions}>
                              <span className={styles.confirmText}>Eliminar?</span>
                              <button
                                type="button"
                                className={styles.dangerButton}
                                onClick={() => void handleDelete(rule.id)}
                                disabled={saving}
                              >
                                {saving ? "…" : "Sim"}
                              </button>
                              <button
                                type="button"
                                className={styles.ghostButton}
                                onClick={() => setDeletingId(null)}
                                disabled={saving}
                              >
                                Não
                              </button>
                            </div>
                          ) : (
                            <div className={styles.rowActions}>
                              <button
                                type="button"
                                className={styles.ghostButton}
                                onClick={() => startEdit(rule)}
                              >
                                Editar
                              </button>
                              <button
                                type="button"
                                className={styles.dangerButton}
                                onClick={() => { setDeletingId(rule.id); setEditingId(null); }}
                              >
                                Eliminar
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
          </div>
        </section>
      </div>
    </div>
  );
}
