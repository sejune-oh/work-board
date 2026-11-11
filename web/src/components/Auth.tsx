"use client";

import { Fragment, useEffect, useRef, useState } from "react";
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

/** The one main action of a gate screen. */
const PRIMARY = "btn btn-primary btn-lg w-full";

/** App problems stay in ink: no vermilion. */
export function Notice({ children }: { children: React.ReactNode }) {
  return (
    <div role="alert" className="rounded-control border-l-2 border-ink-soft bg-sunken px-4 py-3 text-small text-ink">
      {children}
    </div>
  );
}

/** `wb` mark and product name; the dev-mode badge sits beside it when sign-in is off. */
function Brand() {
  const t = useT();
  return (
    <span className="flex min-w-0 items-center gap-2.5">
      <span aria-hidden className="grid h-7 w-7 shrink-0 place-items-center rounded-control bg-ink font-mono text-meta font-semibold text-on-ink">
        wb
      </span>
      <span className="truncate text-ui font-semibold">{t.app.title}</span>
      {!authEnabled && (
        <span className="shrink-0 rounded-flag px-1.5 text-meta leading-5 text-ink-soft shadow-[inset_0_0_0_1px_var(--wb-rule-strong)]">
          {t.gate.devMode}
        </span>
      )}
    </span>
  );
}

/**
 * The frame for everything before the app: product mark, language and theme, one centred column.
 * No navigation, search or shortcuts live here. Focus moves to the title whenever it changes.
 * `aside` fills a second column from 1024px up and is not rendered below that.
 */
export function GateFrame({
  kicker,
  title,
  lead,
  footer,
  aside,
  children,
}: {
  kicker: string;
  title: string;
  lead?: React.ReactNode;
  footer?: React.ReactNode;
  aside?: React.ReactNode;
  children: React.ReactNode;
}) {
  const t = useT();
  const heading = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    heading.current?.focus({ preventScroll: true });
  }, [title]);

  const column = (
    <div className="flex min-h-dvh flex-col">
      <header className="flex items-center gap-3 px-4 py-5 min-[640px]:px-6 min-[1024px]:px-10">
        <Brand />
        <div className="ml-auto flex shrink-0 items-center gap-2">
          <LangToggle />
          <ThemeToggle />
        </div>
      </header>
      <main className="grid flex-1 items-center px-4 pb-16 pt-8 min-[640px]:px-6 min-[1024px]:px-10">
        <div key={title} className="reveal mx-auto w-full max-w-[440px]">
          <p className="flex items-center gap-2 text-small font-medium text-muted">
            <span aria-hidden className="h-px w-4 bg-rule-strong" />
            {kicker}
          </p>
          <h1 ref={heading} tabIndex={-1} style={{ outline: "none" }} className="mt-3 text-display font-semibold">
            {title}
          </h1>
          {lead && <p className="mt-3 text-body text-ink-soft">{lead}</p>}
          <div className="mt-8 rounded-sheet bg-surface p-6 shadow-elev-1 max-[639px]:p-5">{children}</div>
          {footer && <div className="mt-5">{footer}</div>}
        </div>
      </main>
      <footer className="px-4 py-5 text-meta text-muted min-[640px]:px-6 min-[1024px]:px-10">{t.gate.tagline}</footer>
    </div>
  );

  if (!aside) return <div className="bg-canvas">{column}</div>;
  return (
    <div className="grid bg-canvas min-[1024px]:grid-cols-[minmax(0,1fr)_minmax(0,1.05fr)]">
      {column}
      <aside className="hidden content-center gap-5 border-l border-rule bg-sunken px-12 py-16 min-[1024px]:grid">{aside}</aside>
    </div>
  );
}

/** Sign-in screen sample: what the overview says each morning, labelled as an example. */
function SignInPreview() {
  const t = useT();
  const p = t.gate.preview;
  return (
    <>
      <figure aria-label={p.label} className="m-0 max-w-[520px] rounded-sheet bg-surface p-6 shadow-elev-2">
        <div className="flex items-center justify-between gap-3">
          <p className="flex items-center gap-2 text-small font-medium text-muted">
            <span aria-hidden className="h-px w-4 bg-rule-strong" />
            {p.kicker}
          </p>
          <span className="inline-flex h-[22px] items-center rounded-full bg-sunken px-2 text-meta font-semibold text-muted">{p.example}</span>
        </div>
        <p className="mt-2 text-title font-semibold text-balance">
          {p.sentence.map((part, i) =>
            typeof part === "number" ? (
              <em key={i} className="font-mono not-italic text-accent">
                {part}
              </em>
            ) : (
              <Fragment key={i}>{part}</Fragment>
            ),
          )}
        </p>
        <ul className="mt-5">
          {p.rows.map((r) => (
            <li key={r.ref} className="grid grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-3 border-t border-rule py-3 text-ui">
              <span className={`flag ${r.fill ? "flag-fill" : ""}`}>{r.flag}</span>
              <span className="truncate">{r.title}</span>
              <span className="font-mono text-meta text-muted">{r.ref}</span>
            </li>
          ))}
        </ul>
      </figure>
      <p className="max-w-[520px] text-small text-muted">{p.note}</p>
    </>
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
    <p className="flex flex-wrap items-center gap-x-2 gap-y-1 text-small text-muted">
      {login && <span>{t.gate.signedInAs(login)}</span>}
      {login && <span aria-hidden>·</span>}
      <button type="button" onClick={() => void leave()} className="link cursor-pointer">
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
      <button type="button" className={PRIMARY} onClick={start} disabled={busy} aria-busy={busy}>
        <GitHubMark size={18} />
        {busy ? `${t.auth.redirecting}…` : (label ?? t.auth.signIn)}
      </button>
      {failed && <Notice>{t.auth.startFailed}</Notice>}
    </div>
  );
}

export function SignInScreen({ next, ended }: { next: string; ended: boolean }) {
  const t = useT();
  return (
    <GateFrame kicker={t.gate.login.kicker} title={t.start.title} lead={t.start.lead} aside={<SignInPreview />}>
      {ended && (
        <div className="mb-5">
          <Notice>{t.auth.sessionEnded}</Notice>
        </div>
      )}
      <SignInButton returnTo={next} />
      <p className="mt-4 text-small text-muted">{t.auth.scopeNote}</p>
    </GateFrame>
  );
}

export function NoMembershipScreen({ onRecheck, busy }: { onRecheck: () => void; busy: boolean }) {
  const t = useT();
  return (
    <GateFrame kicker={t.gate.company.kicker} title={t.auth.noMembership.title} lead={t.auth.noMembership.lead} footer={<SignedInAs />}>
      <p className="text-body text-ink-soft">{t.auth.noMembership.next}</p>
      <div className="mt-5">
        <button type="button" className={PRIMARY} onClick={onRecheck} disabled={busy} aria-busy={busy}>
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
        <ul className="-m-4 flex flex-col gap-0.5 max-[639px]:-m-3">
          {options.map((o) => (
            <li key={o.id}>
              <button
                type="button"
                onClick={() => setTenant(o.id)}
                className="grid w-full cursor-pointer grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-4 rounded-item p-4 text-left transition-colors duration-[120ms] ease-tool hover:bg-hover focus-visible:bg-hover"
              >
                <span className="grid h-9 w-9 place-items-center rounded-control bg-sunken font-mono text-small font-semibold text-ink-soft">
                  {o.name.slice(0, 2)}
                </span>
                <span className="truncate text-body font-semibold">{o.name}</span>
                <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden className="text-faint">
                  <path d="M6 3.5 10.5 8 6 12.5" />
                </svg>
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
      <div className="mt-5">
        <button type="button" className={PRIMARY} onClick={onRetry} disabled={busy} aria-busy={busy}>
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
      <span className="flex min-w-0 items-center gap-2 text-small font-medium text-ink">
        <span aria-hidden className="grid h-7 w-7 shrink-0 place-items-center rounded-full bg-ink text-meta font-semibold text-on-ink">
          {initials(name)}
        </span>
        <span className="hidden max-w-[10rem] truncate min-[1024px]:inline">{name}</span>
        <span className="sr-only min-[1024px]:hidden">{name}</span>
      </span>
      <button
        type="button"
        onClick={() => void leave()}
        className="h-8 shrink-0 cursor-pointer rounded-control px-2.5 text-meta font-medium text-ink-soft transition-colors duration-[120ms] ease-tool hover:bg-hover hover:text-ink"
      >
        {t.auth.signOut}
      </button>
    </div>
  );
}
