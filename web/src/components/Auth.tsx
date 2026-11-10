"use client";

import { useEffect, useRef, useState } from "react";
import { ApiError, setDevIdentity } from "@/lib/api";
import { authEnabled, markLeaving, setTenant, signInWithGitHub, signOut } from "@/lib/auth";
import { useT } from "@/lib/i18n";
import { useAuthSession, useLogin, useMe } from "@/lib/me";
import { LangToggle } from "./LangToggle";
import { ThemeToggle } from "./ThemeToggle";

export function GitHubMark({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="currentColor" aria-hidden="true" className="shrink-0">
      <path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0016 8c0-4.42-3.58-8-8-8z" />
    </svg>
  );
}

const BUTTON =
  "inline-flex h-10 w-full cursor-pointer items-center justify-center gap-2 rounded-control border border-rule-strong bg-surface px-4 text-ui font-medium text-ink transition-colors duration-[120ms] ease-tool hover:bg-hover disabled:cursor-default disabled:text-muted";

/** App problems stay in ink: no vermilion. */
export function Notice({ children }: { children: React.ReactNode }) {
  return (
    <div role="alert" className="rounded-control border border-rule border-l-2 border-l-ink-soft bg-surface px-4 py-3 text-ui text-ink">
      {children}
    </div>
  );
}

/**
 * The frame for everything before the app: product mark, language and theme, one centred column.
 * No navigation, search or shortcuts live here. Focus moves to the title whenever it changes.
 */
export function GateFrame({
  kicker,
  title,
  lead,
  footer,
  children,
}: {
  kicker: string;
  title: string;
  lead?: React.ReactNode;
  footer?: React.ReactNode;
  children: React.ReactNode;
}) {
  const t = useT();
  const heading = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    heading.current?.focus({ preventScroll: true });
  }, [title]);

  return (
    <div className="flex min-h-screen flex-col bg-canvas">
      <header className="flex h-12 items-center gap-3 px-4 min-[900px]:px-6">
        <span className="flex min-w-0 items-center gap-2">
          <span
            aria-hidden
            className="grid h-[22px] w-[22px] shrink-0 place-items-center rounded-control bg-ink font-mono text-[11px] font-semibold text-surface"
          >
            wb
          </span>
          <span className="truncate text-[13.5px] font-semibold">{t.app.title}</span>
          {!authEnabled && (
            <span className="shrink-0 rounded-[2px] border border-rule-strong px-1.5 font-mono text-[11px] leading-[18px] text-ink-soft">
              {t.gate.devMode}
            </span>
          )}
        </span>
        <div className="ml-auto flex shrink-0 items-center gap-2">
          <LangToggle />
          <ThemeToggle />
        </div>
      </header>
      <main className="flex flex-1 justify-center px-4 pb-16 pt-[clamp(24px,12vh,120px)]">
        <div key={title} className="reveal flex w-full max-w-[400px] flex-col gap-5">
          <div>
            <p className="font-mono text-meta text-muted">{kicker}</p>
            <h1 ref={heading} tabIndex={-1} style={{ outline: "none" }} className="mt-1 text-display font-semibold tracking-[-0.015em]">
              {title}
            </h1>
            {lead && <p className="mt-1 text-body text-ink-soft">{lead}</p>}
          </div>
          <div className="card p-3.5">{children}</div>
          {footer}
        </div>
      </main>
    </div>
  );
}

/** Neutral screen while the session and company are checked; this is also what the static HTML holds. */
export function CheckingScreen({ title }: { title?: string }) {
  const t = useT();
  return (
    <GateFrame kicker={t.gate.checking.kicker} title={title ?? t.gate.checking.title}>
      <p className="text-ui text-muted">{t.gate.checking.lead}</p>
    </GateFrame>
  );
}

/** Clears who is signed in, then lets the gate send the person to `/login` with no return path. */
export async function leave() {
  markLeaving(true);
  if (authEnabled) await signOut();
  else setDevIdentity(null);
}

/** "Signed in as <login> · Sign out" under the card of the company screens. */
function SignedInAs() {
  const t = useT();
  const login = useLogin();
  return (
    <p className="flex flex-wrap items-center gap-x-2 gap-y-1 px-0.5 text-meta text-muted">
      {login && <span>{t.gate.signedInAs(login)}</span>}
      {login && <span aria-hidden>·</span>}
      <button type="button" onClick={() => void leave()} className="link cursor-pointer text-meta">
        {t.auth.signOut}
      </button>
    </p>
  );
}

export function SignInButton({ label, returnTo }: { label?: string; returnTo?: string }) {
  const t = useT();
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);

  const start = async () => {
    setBusy(true);
    setFailed(false);
    try {
      await signInWithGitHub(returnTo);
    } catch {
      setFailed(true);
      setBusy(false);
    }
  };

  return (
    <div className="flex flex-col gap-3">
      <button type="button" className={BUTTON} onClick={start} disabled={busy} aria-busy={busy}>
        <GitHubMark />
        {busy ? `${t.auth.redirecting}…` : (label ?? t.auth.signIn)}
      </button>
      {failed && <Notice>{t.auth.startFailed}</Notice>}
    </div>
  );
}

export function SignInScreen({ next, ended }: { next: string; ended: boolean }) {
  const t = useT();
  return (
    <GateFrame kicker={t.gate.login.kicker} title={t.start.title} lead={t.start.lead}>
      {ended && (
        <div className="mb-3.5">
          <Notice>{t.auth.sessionEnded}</Notice>
        </div>
      )}
      <p className="mb-3 text-ui text-ink-soft">{t.auth.lead}</p>
      <SignInButton returnTo={next} />
      <p className="mt-3 text-meta text-muted">{t.auth.scopeNote}</p>
    </GateFrame>
  );
}

export function NoMembershipScreen({ onRecheck, busy }: { onRecheck: () => void; busy: boolean }) {
  const t = useT();
  return (
    <GateFrame kicker={t.gate.company.kicker} title={t.auth.noMembership.title} footer={<SignedInAs />}>
      <p className="text-body text-ink">{t.auth.noMembership.lead}</p>
      <p className="mt-1 text-body text-ink-soft">{t.auth.noMembership.next}</p>
      <div className="mt-4">
        <button type="button" className={BUTTON} onClick={onRecheck} disabled={busy} aria-busy={busy}>
          {busy ? `${t.gate.error.retrying}…` : t.auth.noMembership.recheck}
        </button>
      </div>
    </GateFrame>
  );
}

type TenantOption = { id: string; name: string };

/** The API may list the person's companies with `tenant_required`; without a list we can only say so. */
function tenantOptions(error: ApiError): TenantOption[] {
  const list = (error.body as { tenants?: unknown } | null)?.tenants;
  if (!Array.isArray(list)) return [];
  return list.filter(
    (x): x is TenantOption => typeof x === "object" && x !== null && typeof x.id === "string" && typeof x.name === "string",
  );
}

export function TenantChoiceScreen({ error }: { error: ApiError }) {
  const t = useT();
  const options = tenantOptions(error);
  return (
    <GateFrame kicker={t.gate.company.kicker} title={t.auth.tenant.title} lead={t.auth.tenant.lead} footer={<SignedInAs />}>
      {options.length > 0 ? (
        <ul className="-m-3.5">
          {options.map((o, i) => (
            <li key={o.id} className={i > 0 ? "border-t border-rule" : undefined}>
              <button
                type="button"
                onClick={() => setTenant(o.id)}
                className="flex h-11 w-full cursor-pointer items-center gap-3 px-3.5 text-left text-ui font-medium text-ink hover:bg-hover focus-visible:bg-hover"
              >
                <span className="grid h-[22px] w-[22px] shrink-0 place-items-center rounded-control bg-sunken font-mono text-[11px] font-semibold text-ink-soft">
                  {o.name.slice(0, 2)}
                </span>
                <span className="min-w-0 truncate">{o.name}</span>
                <span aria-hidden className="ml-auto text-muted">
                  →
                </span>
              </button>
            </li>
          ))}
        </ul>
      ) : (
        <p className="text-ui text-ink">{t.auth.tenant.noList}</p>
      )}
    </GateFrame>
  );
}

/** Network or server trouble while checking: the person is not treated as signed out. */
export function GateErrorScreen({ error, onRetry, busy }: { error: Error; onRetry: () => void; busy: boolean }) {
  const t = useT();
  const detail = error instanceof ApiError ? t.gate.error.status(error.status) : t.gate.error.network;
  return (
    <GateFrame kicker={t.gate.error.kicker} title={t.gate.error.title} lead={t.gate.error.lead}>
      <p className="border-l-2 border-ink-soft pl-3 text-ui text-ink">{detail}</p>
      <div className="mt-4">
        <button type="button" className={BUTTON} onClick={onRetry} disabled={busy} aria-busy={busy}>
          {busy ? `${t.gate.error.retrying}…` : t.gate.error.retry}
        </button>
      </div>
    </GateFrame>
  );
}

function initials(name: string) {
  return name.replace(/^@/, "").slice(0, 2).toUpperCase();
}

/** Top bar: who is signed in, and the way out. */
export function AccountMenu() {
  const t = useT();
  const session = useAuthSession();
  const me = useMe().data;
  if (!session) return null;
  const name =
    me?.member.displayName ?? me?.member.githubLogin ?? (session.user.user_metadata?.user_name as string | undefined) ?? session.user.email ?? "";
  return (
    <div className="flex items-center gap-2" aria-label={t.auth.account} role="group">
      <span className="flex min-w-0 items-center gap-1.5 text-ui font-medium text-ink">
        <span
          aria-hidden
          className="grid h-[22px] w-[22px] shrink-0 place-items-center rounded-full bg-sunken font-mono text-[10.5px] font-semibold text-ink-soft"
        >
          {initials(name)}
        </span>
        <span className="hidden max-w-[10rem] truncate sm:inline">{name}</span>
        <span className="sr-only sm:hidden">{name}</span>
      </span>
      <button
        type="button"
        onClick={() => void leave()}
        className="h-[26px] shrink-0 cursor-pointer rounded-control border border-rule px-2 text-meta text-ink-soft transition-colors duration-[120ms] ease-tool hover:border-rule-strong hover:text-ink"
      >
        {t.auth.signOut}
      </button>
    </div>
  );
}
