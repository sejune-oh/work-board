"use client";

import { useState } from "react";
import { ApiError } from "@/lib/api";
import { setTenant, signInWithGitHub, signOut } from "@/lib/auth";
import { useT } from "@/lib/i18n";
import { useAuthSession, useMe } from "@/lib/me";

export function GitHubMark({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="currentColor" aria-hidden="true" className="shrink-0">
      <path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0016 8c0-4.42-3.58-8-8-8z" />
    </svg>
  );
}

const BUTTON =
  "inline-flex h-10 cursor-pointer items-center justify-center gap-2 rounded-control border border-rule-strong bg-surface px-4 text-ui font-medium text-ink transition-colors duration-[120ms] ease-tool hover:bg-hover disabled:cursor-default disabled:text-muted";

/** App problems stay in ink: no vermilion. */
export function Notice({ children }: { children: React.ReactNode }) {
  return (
    <div role="alert" className="rounded-control border border-rule border-l-2 border-l-ink-soft bg-surface px-4 py-3 text-ui text-ink">
      {children}
    </div>
  );
}

function ScreenHead({ title, lead }: { title: string; lead?: React.ReactNode }) {
  const t = useT();
  return (
    <div>
      <p className="font-mono text-meta text-muted">{t.app.title}</p>
      <h1 className="mt-1 text-display font-semibold tracking-[-0.015em]">{title}</h1>
      {lead && <p className="mt-1 text-body text-ink-soft">{lead}</p>}
    </div>
  );
}

export function Screen({ children }: { children: React.ReactNode }) {
  return <div className="reveal mx-auto flex max-w-[560px] flex-col gap-6 py-6 min-[900px]:py-12">{children}</div>;
}

export function SignInButton({ label }: { label?: string }) {
  const t = useT();
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);

  const start = async () => {
    setBusy(true);
    setFailed(false);
    try {
      await signInWithGitHub();
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

/** Shown instead of the boards while nobody is signed in. */
export function SignInScreen({ ended = false }: { ended?: boolean }) {
  const t = useT();
  return (
    <Screen>
      <ScreenHead title={t.start.title} lead={t.start.lead} />
      {ended && <Notice>{t.auth.sessionEnded}</Notice>}
      <div className="card p-4">
        <p className="mb-3 text-ui text-ink-soft">{t.auth.lead}</p>
        <SignInButton />
        <p className="mt-3 text-meta text-muted">{t.auth.scopeNote}</p>
      </div>
    </Screen>
  );
}

export function NoMembershipScreen() {
  const t = useT();
  const session = useAuthSession();
  const login = session?.user.user_metadata?.user_name as string | undefined;
  return (
    <Screen>
      <ScreenHead title={t.auth.noMembership.title} />
      <div className="card p-4">
        <p className="text-body text-ink">
          {login ? t.auth.noMembership.lead(login) : t.auth.noMembership.leadNoLogin}
        </p>
        <p className="mt-1 text-body text-ink-soft">{t.auth.noMembership.next}</p>
        <div className="mt-4 border-t border-rule pt-4">
          <button type="button" className={BUTTON} onClick={() => void signOut()}>
            {t.auth.noMembership.otherAccount}
          </button>
        </div>
      </div>
    </Screen>
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
    <Screen>
      <ScreenHead title={t.auth.tenant.title} lead={t.auth.tenant.lead} />
      {options.length > 0 ? (
        <ul className="card">
          {options.map((o, i) => (
            <li key={o.id} className={i > 0 ? "border-t border-rule" : undefined}>
              <button
                type="button"
                data-row
                onClick={() => setTenant(o.id)}
                className="flex h-11 w-full cursor-pointer items-center gap-3 px-3.5 text-left text-ui font-medium text-ink hover:bg-hover focus-visible:bg-hover"
              >
                <span className="grid h-[22px] w-[22px] shrink-0 place-items-center rounded-control bg-sunken font-mono text-[11px] font-semibold text-ink-soft">
                  {o.name.slice(0, 2)}
                </span>
                <span className="min-w-0 truncate">{o.name}</span>
                <span aria-hidden className="ml-auto text-muted">→</span>
              </button>
            </li>
          ))}
        </ul>
      ) : (
        <Notice>{t.auth.tenant.noList}</Notice>
      )}
      <div>
        <button type="button" className={BUTTON} onClick={() => void signOut()}>
          {t.auth.signOut}
        </button>
      </div>
    </Screen>
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
        onClick={() => void signOut()}
        className="h-[26px] shrink-0 cursor-pointer rounded-control border border-rule px-2 text-meta text-ink-soft transition-colors duration-[120ms] ease-tool hover:border-rule-strong hover:text-ink"
      >
        {t.auth.signOut}
      </button>
    </div>
  );
}
