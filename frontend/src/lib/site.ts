export const SITE_URL = (process.env.NEXT_PUBLIC_SITE_URL ?? "https://stmstmstmstmstm.ru").replace(/\/$/, "");
export const SITE_NAME = "СвайпКино";

export function absoluteUrl(path = "/") {
  return new URL(path, `${SITE_URL}/`).toString();
}

export function truncateDescription(value: string | null | undefined, fallback: string) {
  const clean = value?.replace(/\s+/g, " ").trim();
  if (!clean) return fallback;
  return clean.length > 160 ? `${clean.slice(0, 157).trimEnd()}…` : clean;
}
