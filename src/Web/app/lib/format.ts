export function formatCurrency(value: number, currency = "EUR") {
  return new Intl.NumberFormat("pt-PT", {
    style: "currency",
    currency,
    maximumFractionDigits: 2,
  }).format(value);
}

export function formatDate(value: string) {
  return new Intl.DateTimeFormat("pt-PT", {
    day: "2-digit",
    month: "short",
    year: "numeric",
  }).format(new Date(`${value}T00:00:00`));
}

export function monthKey(date: Date) {
  const year = date.getUTCFullYear();
  const month = String(date.getUTCMonth() + 1).padStart(2, "0");
  return `${year}-${month}`;
}

export function last12Months() {
  const months: string[] = [];
  const today = new Date();

  for (let offset = 0; offset < 12; offset += 1) {
    const date = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth() - offset, 1));
    months.push(monthKey(date));
  }

  return months;
}

export function getAllAvailableMonths() {
  const months: string[] = [];
  const today = new Date();
  const startYear = 2012;
  const startMonth = 0;

  const startDate = new Date(Date.UTC(startYear, startMonth, 1));
  const current = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), 1));

  while (current >= startDate) {
    months.push(monthKey(current));
    current.setUTCMonth(current.getUTCMonth() - 1);
  }

  return months;
}

export function formatMonthLabel(month: string) {
  const [year, numericMonth] = month.split("-").map(Number);
  return new Intl.DateTimeFormat("pt-PT", {
    month: "long",
    year: "numeric",
  }).format(new Date(Date.UTC(year, numericMonth - 1, 1)));
}

export function formatFileSize(bytes: number) {
  if (bytes < 1024) {
    return `${bytes} B`;
  }

  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toFixed(1)} KB`;
  }

  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export function getMonthsInRange(fromDate: Date, toDate: Date): string[] {
  const months: string[] = [];
  const current = new Date(Date.UTC(fromDate.getUTCFullYear(), fromDate.getUTCMonth(), 1));
  const end = new Date(Date.UTC(toDate.getUTCFullYear(), toDate.getUTCMonth(), 1));

  while (current <= end) {
    months.push(monthKey(current));
    current.setUTCMonth(current.getUTCMonth() + 1);
  }

  return months;
}

export function getDateRangeMonths(days: number | null, preset?: string): string[] {
  const today = new Date();
  let startDate: Date;

  if (preset === "3m") {
    startDate = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth() - 2, 1));
  } else if (preset === "6m") {
    startDate = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth() - 5, 1));
  } else if (preset === "1y") {
    startDate = new Date(Date.UTC(today.getUTCFullYear() - 1, today.getUTCMonth(), 1));
  } else if (preset === "all") {
    startDate = new Date(Date.UTC(2012, 0, 1));
  } else if (days && days > 0) {
    startDate = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), today.getUTCDate() - days));
  } else {
    startDate = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), 1));
  }

  return getMonthsInRange(startDate, today);
}

export function getDateRangeFromMonths(months: string[]): { fromDate: string; toDate: string } | null {
  if (months.length === 0) return null;

  const firstMonth = months[0];
  const lastMonth = months[months.length - 1];

  const fromDate = `${firstMonth}-01`;

  const [year, month] = lastMonth.split("-").map(Number);
  const lastDay = new Date(Date.UTC(year, month, 0)).getUTCDate();
  const toDate = `${lastMonth}-${String(lastDay).padStart(2, "0")}`;

  return { fromDate, toDate };
}

export type TrendChartData = {
  month: string;
  credits: number;
  debits: number;
};

export function calculateMonthlyTrend(transactions: Array<{ bookingDate: string; direction: string; amount: number }>): TrendChartData[] {
  const monthMap = new Map<string, { credits: number; debits: number }>();

  for (const txn of transactions) {
    const [year, month] = txn.bookingDate.split("-").slice(0, 2);
    const monthKey = `${year}-${month}`;

    if (!monthMap.has(monthKey)) {
      monthMap.set(monthKey, { credits: 0, debits: 0 });
    }

    const data = monthMap.get(monthKey)!;
    const amount = Number(txn.amount);
    if (txn.direction === "IN") {
      data.credits += amount;
    } else {
      data.debits += Math.abs(amount);
    }
  }

  const sorted = Array.from(monthMap.entries())
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([month, { credits, debits }]) => ({
      month: formatMonthLabel(month).substring(0, 3),
      credits: Math.round(credits),
      debits: Math.round(debits),
    }));

  return sorted;
}
