export type AccountBalance = {
  id: number;
  name: string;
  source: string;
  currency: string;
  balance: number;
};

export type CategorySpend = {
  category: string;
  total: number;
};

export type SpendingByCategoryResponse = {
  month: string;
  categories: CategorySpend[];
  totals: {
    credits: number;
    debits: number;
  };
};

export type MonthlyCategorySummary = {
  name: string;
  totalOut: number;
  count: number;
};

export type MonthlyMerchantSummary = {
  name: string;
  totalOut: number;
  count: number;
};

export type MonthlyAggregations = {
  month: string;
  totalOut: number;
  totalIn: number;
  transactionCount: number;
  priorMonthTotalOut: number | null;
  topCategories: MonthlyCategorySummary[];
  topMerchants: MonthlyMerchantSummary[];
};

export type MonthlyAnomaly = {
  transactionId: number;
  normalizedMerchant: string | null;
  rawDescription: string;
  amount: number;
  direction: "IN" | "OUT";
  category: string | null;
  bookingDate: string;
  deviationFactor: number;
};

export type MonthlyReport = {
  month: string;
  aggregations: MonthlyAggregations;
  anomalies: MonthlyAnomaly[];
  report: string | null;
};

export type DashboardTransaction = {
  id: number;
  bookingDate: string;
  normalizedMerchant: string;
  rawDescription: string;
  amount: number;
  direction: "IN" | "OUT";
  currency: string;
  status: "completed" | "refunded" | "cancelled" | "pending";
  category: string;
  accountName: string;
  source: string;
};

export type ReviewTransaction = {
  id: number;
  date: string;
  normalizedMerchant: string | null;
  rawDescription: string;
  amount: number;
  accountId: number;
  categoryCanonicalId: number | null;
  categoryName: string | null;
  currency: string;
};

export type RuleSuggestion = {
  id: number;
  transactionId: number;
  normalizedMerchant: string | null;
  suggestedPattern: string;
  suggestedMatchType: "merchant_eq" | "contains" | "regex";
  categoryCanonicalId: number;
  categoryName: string;
  confidence: number;
};

export type Category = {
  id: number;
  name: string;
};

export type UpdateTransactionCategoryInput = {
  categoryId: number;
  createRule: boolean;
  matchType?: "merchant_eq" | "contains" | "regex";
  pattern?: string;
};

export type ImportResult = {
  imported: number;
  ignored: number;
  byStatus: Record<string, number>;
  source: string;
  accountId: number;
  batchId: number;
};

type TransactionFilters = {
  month?: string;
  fromDate?: string;
  toDate?: string;
  account?: string;
  category?: string;
  search?: string;
};

async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: {
      Accept: "application/json",
      ...(init?.headers ?? {}),
    },
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(`Request failed: ${response.status}`);
  }

  return (await response.json()) as T;
}

async function apiSend(path: string, init: RequestInit) {
  const response = await fetch(path, {
    ...init,
    headers: {
      Accept: "application/json",
      ...(init.headers ?? {}),
    },
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(`Request failed: ${response.status}`);
  }
}

async function readApiError(response: Response) {
  try {
    const payload = (await response.json()) as { error?: string; title?: string; detail?: string };
    if (payload.error) {
      return payload.error;
    }
    // ASP.NET Results.Problem payloads carry title/detail instead of error
    if (payload.title || payload.detail) {
      return [payload.title, payload.detail].filter(Boolean).join(": ");
    }
  } catch {
    // Ignore JSON parse errors and fall back to status text.
  }

  return `Request failed: ${response.status}`;
}

export function getAccounts() {
  return apiFetch<AccountBalance[]>("/api/accounts");
}

export function getSpendingByCategory(month: string) {
  const query = new URLSearchParams({ month });
  return apiFetch<SpendingByCategoryResponse>(`/api/spending-by-category?${query.toString()}`);
}

export type MonthlyTrendData = {
  month: string;
  credits: number;
  debits: number;
};

export function getMonthlyTrend(range?: { fromDate?: string; toDate?: string }) {
  const query = new URLSearchParams();
  if (range?.fromDate) query.set("fromDate", range.fromDate);
  if (range?.toDate) query.set("toDate", range.toDate);
  const suffix = query.size > 0 ? `?${query.toString()}` : "";
  return apiFetch<MonthlyTrendData[]>(`/api/monthly-trend${suffix}`);
}

export function getMonthlyReport(month: string) {
  const query = new URLSearchParams({ month });
  return apiFetch<MonthlyReport>(`/api/reports/monthly?${query.toString()}`);
}

export function getTransactions(filters: TransactionFilters = {}) {
  const query = new URLSearchParams();

  if (filters.month) {
    query.set("month", filters.month);
  }

  if (filters.fromDate) {
    query.set("fromDate", filters.fromDate);
  }

  if (filters.toDate) {
    query.set("toDate", filters.toDate);
  }

  if (filters.account) {
    query.set("account", filters.account);
  }

  if (filters.category) {
    query.set("category", filters.category);
  }

  if (filters.search) {
    query.set("search", filters.search);
  }

  const suffix = query.size > 0 ? `?${query.toString()}` : "";
  return apiFetch<DashboardTransaction[]>(`/api/transactions${suffix}`);
}

export function getReviewTransactions() {
  return apiFetch<ReviewTransaction[]>("/api/review/transactions");
}

export function getRuleSuggestions() {
  return apiFetch<RuleSuggestion[]>("/api/review/rule-suggestions");
}

export function getCategories() {
  return apiFetch<Category[]>("/api/categories");
}

export function patchTransactionCategory(id: number, body: UpdateTransactionCategoryInput) {
  return apiSend(`/api/transactions/${id}/category`, {
    method: "PATCH",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(body),
  });
}

export type RuleSuggestionPreview = {
  pattern: string;
  matchType: string;
  matches: {
    id: number;
    bookingDate: string;
    normalizedMerchant: string | null;
    rawDescription: string;
    amount: number;
    direction: "IN" | "OUT";
    currency: string;
    isOrigin: boolean;
  }[];
};

export type TransferCandidate = {
  outId: number;
  outDate: string;
  amount: number;
  outSource: string;
  outAccount: string;
  outCurrency: string;
  inId: number;
  inDate: string;
  inSource: string;
  inAccount: string;
  inCurrency: string;
};

export function approveRuleSuggestion(
  id: number,
  overrides?: { pattern?: string; matchType?: string; categoryId?: number },
) {
  return apiFetch<{ recategorizedTransactions: number }>(
    `/api/review/rule-suggestions/${id}/approve`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(overrides ?? {}),
    },
  );
}

export function previewRuleSuggestion(id: number) {
  return apiFetch<RuleSuggestionPreview>(`/api/review/rule-suggestions/${id}/preview`);
}

export function getTransferCandidates() {
  return apiFetch<TransferCandidate[]>("/api/review/transfer-candidates");
}

export function markTransfers(transactionIds: number[], mark: boolean) {
  return apiFetch<{ markedTransactions: number }>("/api/review/transfer-candidates/mark", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ transactionIds, mark }),
  });
}

export function rejectRuleSuggestion(id: number) {
  return apiSend(`/api/review/rule-suggestions/${id}/reject`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({}),
  });
}

export type Rule = {
  id: number;
  pattern: string;
  matchType: "merchant_eq" | "contains" | "regex";
  priority: number;
  categoryId: number;
  categoryName: string;
};

export function getRules() {
  return apiFetch<Rule[]>("/api/rules");
}

export function createRule(body: { pattern: string; matchType: string; categoryId: number }) {
  return apiFetch<{ recategorizedTransactions: number }>("/api/rules", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
}

export function updateRule(id: number, body: { pattern: string; matchType: string; categoryId: number; priority: number }) {
  return apiFetch<{ recategorizedTransactions: number }>(`/api/rules/${id}`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
}

export function deleteRule(id: number) {
  return apiSend(`/api/rules/${id}`, { method: "DELETE" });
}

export function createCategory(name: string) {
  return apiFetch<Category>("/api/categories", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ name }),
  });
}

export function resetDatabase() {
  return apiSend("/api/debug/reset", { method: "POST" });
}

export async function updateTransactionCategory(
  transactionId: number,
  categoryId: number,
  options?: { createRule?: boolean; pattern?: string; matchType?: string }
) {
  return apiSend(`/api/transactions/${transactionId}`, {
    method: "PATCH",
    body: JSON.stringify({
      categoryId,
      createRule: options?.createRule ?? false,
      pattern: options?.pattern,
      matchType: options?.matchType,
    }),
  });
}

export async function uploadImport(file: File) {
  const formData = new FormData();
  formData.append("file", file);

  const response = await fetch("/api/imports", {
    method: "POST",
    body: formData,
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(await readApiError(response));
  }

  return (await response.json()) as ImportResult;
}
