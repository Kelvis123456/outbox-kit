// Set SITE_URL at build time once the real address is known. Canonical, sitemap and OG tags read it.
export const SITE_URL = (process.env.SITE_URL ?? "https://outbox-kit.vercel.app").replace(/\/$/, "");
export const REPO_URL = "https://github.com/Kelvis123456/outbox-kit";
export const DECISIONS_URL = `${REPO_URL}/blob/main/docs/decisions.md`;
export const TITLE = "outbox-kit: transactional outbox for EF Core and PostgreSQL";
export const DESCRIPTION =
  "A small .NET library for the transactional outbox pattern on EF Core and PostgreSQL, plus an idempotent consumer. Tested against a real Postgres, including a killed process.";
