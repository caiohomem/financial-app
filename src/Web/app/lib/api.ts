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

type TransactionFilters = {
  month?: string;
  account?: string;
  category?: string;
  search?: string;
};

async function apiFetch<T>(path: string): Promise<T> {
  const response = await fetch(path, {
    headers: {
      Accept: "application/json",
    },
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(`Request failed: ${response.status}`);
  }

  return (await response.json()) as T;
}

export function getAccounts() {
  return apiFetch<AccountBalance[]>("/api/accounts");
}

export function getSpendingByCategory(month: string) {
  const query = new URLSearchParams({ month });
  return apiFetch<SpendingByCategoryResponse>(`/api/spending-by-category?${query.toString()}`);
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
