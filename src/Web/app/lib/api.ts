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

type TransactionFilters = {
  month?: string;
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
      "Content-Type": "application/json",
      ...(init.headers ?? {}),
    },
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(`Request failed: ${response.status}`);
  }
}

export function getAccounts() {
  return apiFetch<AccountBalance[]>("/api/accounts");
}

export function getSpendingByCategory(month: string) {
  const query = new URLSearchParams({ month });
  return apiFetch<SpendingByCategoryResponse>(`/api/spending-by-category?${query.toString()}`);
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
    body: JSON.stringify(body),
  });
}

export function approveRuleSuggestion(id: number) {
  return apiSend(`/api/review/rule-suggestions/${id}/approve`, {
    method: "POST",
    body: JSON.stringify({}),
  });
}

export function rejectRuleSuggestion(id: number) {
  return apiSend(`/api/review/rule-suggestions/${id}/reject`, {
    method: "POST",
    body: JSON.stringify({}),
  });
}
