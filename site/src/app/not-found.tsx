import type { Metadata } from "next";
import Link from "next/link";

export const metadata: Metadata = { title: "Page not found | outbox-kit", robots: { index: false } };

export default function NotFound() {
  return (
    <main id="main" className="wrap section min-h-[100dvh]">
      <p className="mono m-0 text-sm text-muted">404</p>
      <h1 className="mt-3 text-5xl font-extrabold">This page was never published.</h1>
      <p className="mt-5 max-w-[48ch] text-muted">
        There is only one page here, and it covers the whole library. Nothing is queued for this address.
      </p>
      <p className="mt-8">
        <Link href="/" className="btn btn-solid">
          Back to outbox-kit
        </Link>
      </p>
    </main>
  );
}
