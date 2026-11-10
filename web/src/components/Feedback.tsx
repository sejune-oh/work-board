"use client";

import { ApiError } from "@/lib/api";
import { authEnabled } from "@/lib/auth";
import { useT } from "@/lib/i18n";

export function Loading({ label }: { label?: string }) {
  const t = useT();
  return <p className="px-5 py-5 text-ui text-muted">{label ?? t.common.loading}…</p>;
}

/** Errors are about the app, not the work, so they stay in ink: no vermilion here. */
export function ErrorBox({ error }: { error: Error }) {
  const t = useT();
  const message =
    error instanceof ApiError && error.status === 401
      ? authEnabled
        ? t.errors.signInAgain
        : t.errors.pickUser
      : error instanceof ApiError && error.status === 404
        ? t.errors.notFound
        : t.errors.noServer;
  return (
    <div role="alert" className="rounded-control border-l-2 border-ink-soft bg-surface px-4 py-3 text-ui text-ink shadow-elev-1">
      {message}
    </div>
  );
}

export function Empty({ children }: { children: React.ReactNode }) {
  return <p className="rounded-item border border-dashed border-rule-strong px-4 py-3 text-small text-muted">{children}</p>;
}
