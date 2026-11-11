"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { GitHubMark } from "@/components/Auth";
import { ConnectionRow, githubInstallUrl } from "@/components/Connections";
import { PageHeader } from "@/components/ui";
import { ApiError, apiPost } from "@/lib/api";
import { type Dict, useT } from "@/lib/i18n";
import type { ConnectGithubError, ConnectGithubRequest, GithubConnection } from "@/lib/types";

type Reason = keyof Dict["connect"]["errors"];
type State =
  | { kind: "working" }
  | { kind: "done"; connection: GithubConnection }
  | { kind: "failed"; reason: Reason; code: string | null; retry: boolean };

const REDIRECT_MS = 3000;

/**
 * One request per GitHub code for the life of the page, so a remounted or re-run effect (React strict
 * mode, Suspense) reuses it instead of spending the single-use code twice.
 */
const pending = new Map<string, Promise<GithubConnection>>();

function connect(body: ConnectGithubRequest): Promise<GithubConnection> {
  let p = pending.get(body.code);
  if (!p) {
    p = apiPost<GithubConnection>("/api/connections/github", body);
    pending.set(body.code, p);
  }
  return p;
}

/** Codes with their own copy; `unauthenticated` is left to the sign-in gate. */
const KNOWN: ReadonlySet<string> = new Set<Exclude<ConnectGithubError, "unauthenticated">>([
  "invalid_request",
  "invalid_code",
  "admin_only",
  "not_your_account",
  "not_your_installation",
  "account_connected",
  "organization_not_supported",
  "github_unavailable",
  "github_app_not_configured",
]);

/** Failures where sending the same code again can work. */
const RETRYABLE: ReadonlySet<Reason> = new Set<Reason>(["github_unavailable", "network", "unknown"]);

/** Failures a fresh install round can fix; the rest need someone else to act first. */
const RESTARTABLE: ReadonlySet<Reason> = new Set<Reason>([
  "invalid_request",
  "invalid_code",
  "not_your_account",
  "not_your_installation",
  "organization_not_supported",
  "missing_params",
  "setup_request",
]);

function reasonOf(e: unknown): State {
  if (e instanceof ApiError) {
    const reason = e.code && KNOWN.has(e.code) ? (e.code as Reason) : "unknown";
    return { kind: "failed", reason, code: e.code ?? String(e.status), retry: RETRYABLE.has(reason) };
  }
  return { kind: "failed", reason: "network", code: null, retry: true };
}

/** Parses GitHub's return address; `null` means it cannot be used. */
function readParams(params: URLSearchParams): ConnectGithubRequest | "request" | null {
  if (params.get("setup_action") === "request") return "request";
  const code = params.get("code");
  const raw = params.get("installation_id");
  const installationId = raw && /^\d+$/.test(raw) ? Number(raw) : NaN;
  if (!code || !Number.isSafeInteger(installationId) || installationId <= 0) return null;
  return { code, installationId };
}

/** GitHub sends the browser here after the App is installed; the server checks it and saves the connection. */
export default function ConnectGithubPage() {
  const t = useT();
  const ct = t.connect;
  const router = useRouter();
  const params = useSearchParams();
  const input = readParams(new URLSearchParams(params.toString()));
  const code = typeof input === "object" && input ? input.code : null;
  const installationId = typeof input === "object" && input ? input.installationId : null;
  const [attempt, setAttempt] = useState(0);
  const [state, setState] = useState<State>({ kind: "working" });
  const title = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    if (code === null || installationId === null) return;
    let cancelled = false;
    connect({ code, installationId })
      .then((connection) => {
        if (!cancelled) setState({ kind: "done", connection });
      })
      .catch((e: unknown) => {
        if (!cancelled) setState(reasonOf(e));
      });
    return () => {
      cancelled = true;
    };
  }, [code, installationId, attempt]);

  useEffect(() => {
    if (state.kind !== "done") return;
    const id = window.setTimeout(() => router.replace("/settings"), REDIRECT_MS);
    return () => window.clearTimeout(id);
  }, [state.kind, router]);

  const view: State =
    input === "request"
      ? { kind: "failed", reason: "setup_request", code: null, retry: false }
      : input === null
        ? { kind: "failed", reason: "missing_params", code: null, retry: false }
        : state;

  useEffect(() => {
    title.current?.focus();
  }, [view.kind]);

  const retry = () => {
    if (code) pending.delete(code);
    setState({ kind: "working" });
    setAttempt((n) => n + 1);
  };

  const heading =
    view.kind === "working"
      ? ct.workingTitle
      : view.kind === "done"
        ? ct.doneTitle
        : view.reason === "setup_request"
          ? ct.pendingTitle
          : ct.failedTitle;
  const lead =
    view.kind === "working"
      ? ct.workingLead
      : view.kind === "done"
        ? ct.doneLead
        : view.reason === "setup_request"
          ? ct.pendingLead
          : ct.failedLead;

  return (
    <div className="flex max-w-[720px] flex-col gap-6">
      <PageHeader
        meta={ct.kicker}
        title={
          <span ref={title} tabIndex={-1} style={{ outline: "none" }}>
            {heading}
          </span>
        }
      >
        {lead}
      </PageHeader>

      <div className="card reveal mt-2 [animation-delay:30ms]">
        {view.kind === "working" && (
          <p role="status" className="flex items-center gap-2.5 px-5 py-5 text-ui text-muted">
            <GitHubMark />
            {ct.workingTitle}
          </p>
        )}

        {view.kind === "done" && (
          <>
            <ul>
              <ConnectionRow connection={view.connection} />
            </ul>
            <div className="flex flex-wrap items-center gap-x-4 gap-y-3 px-5 pb-5 pt-1">
              <Link href="/settings" className="btn btn-primary">
                {ct.toSettings}
              </Link>
              <span role="status" className="text-small text-muted">
                {ct.redirecting}
              </span>
            </div>
          </>
        )}

        {view.kind === "failed" && (
          <div className="flex flex-col gap-4 p-5">
            <div role="alert" className="rounded-control border-l-2 border-ink-soft bg-sunken px-4 py-3">
              <p className="text-ui text-ink">{ct.errors[view.reason][0]}</p>
              <p className="mt-1 text-small text-ink-soft">{ct.errors[view.reason][1]}</p>
              {view.code && (
                <p className="mt-2 text-meta text-muted">
                  {ct.code} <span className="font-mono">{view.code}</span>
                </p>
              )}
            </div>
            <div className="flex flex-wrap items-center gap-x-4 gap-y-3">
              {view.retry ? (
                <button type="button" className="btn btn-primary" onClick={retry}>
                  {ct.retry}
                </button>
              ) : (
                RESTARTABLE.has(view.reason) &&
                githubInstallUrl && (
                  <a href={githubInstallUrl} className="btn btn-primary">
                    <GitHubMark />
                    {t.connections.reconnect}
                  </a>
                )
              )}
              <Link href="/settings" className="link text-ui">
                {ct.back}
              </Link>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
