"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useRef, useState } from "react";
import { authEnabled } from "@/lib/auth";
import { type Dict, useT } from "@/lib/i18n";
import { useMe } from "@/lib/me";
import { AccountMenu } from "./Auth";
import { IssuePanel } from "./IssuePanel";
import { LangToggle } from "./LangToggle";
import { ThemeToggle } from "./ThemeToggle";
import { Kbd } from "./ui";
import { UserPicker } from "./UserPicker";
import { useShortcuts } from "./useShortcuts";

const NAV: { href: string; label: keyof Dict["nav"]; key: string }[] = [
  { href: "/", label: "overview", key: "o" },
  { href: "/me", label: "me", key: "m" },
  { href: "/people", label: "people", key: "p" },
  { href: "/projects", label: "projects", key: "b" },
  { href: "/notes", label: "notes", key: "n" },
  { href: "/settings", label: "settings", key: "s" },
];

function isActive(pathname: string, href: string) {
  return href === "/" ? pathname === "/" : pathname === href || pathname.startsWith(`${href}/`);
}

function screenLabel(pathname: string, t: Dict) {
  if (pathname.startsWith("/search")) return t.nav.search;
  const item = NAV.find((n) => isActive(pathname, n.href));
  return item ? t.nav[item.label] : t.app.title;
}

/** The app chrome. Only the sign-in gate renders it, once `/api/me` has succeeded. */
export function AppShell({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const router = useRouter();
  const [q, setQ] = useState("");
  const searchRef = useRef<HTMLInputElement>(null);
  const org = useMe().data?.tenant.name ?? "";
  const t = useT();

  useShortcuts(searchRef);

  return (
    <div className="flex min-h-screen">
      <aside className="sticky top-0 hidden h-screen w-[224px] shrink-0 flex-col gap-4 border-r border-rule bg-surface px-2.5 py-3 min-[900px]:flex">
        <Link href="/" className="flex items-center gap-2 rounded-control px-2 py-1.5 hover:bg-hover">
          <span className="grid h-[22px] w-[22px] shrink-0 place-items-center rounded-control bg-ink font-mono text-[11px] font-semibold text-surface">
            {org.slice(0, 2) || "wb"}
          </span>
          <span className="min-w-0 leading-tight">
            <span className="block truncate text-[13.5px] font-semibold">{org}</span>
            <span className="block font-mono text-[11px] text-muted">{t.app.title}</span>
          </span>
        </Link>
        <nav aria-label={t.nav.screens}>
          <p className="px-2 pb-1 font-mono text-[11px] text-muted">{t.nav.screens}</p>
          <ul className="flex flex-col gap-px">
            {NAV.map((item) => {
              const active = isActive(pathname, item.href);
              return (
                <li key={item.href}>
                  <Link
                    href={item.href}
                    aria-current={active ? "page" : undefined}
                    aria-keyshortcuts={`g ${item.key}`}
                    className={`group flex h-[30px] items-center gap-2 rounded-control px-2 text-ui transition-colors duration-[120ms] ease-tool ${
                      active ? "bg-sunken font-semibold text-ink" : "font-medium text-ink-soft hover:bg-hover hover:text-ink"
                    }`}
                  >
                    {t.nav[item.label]}
                    <Kbd className="ml-auto opacity-0 transition-opacity duration-[120ms] group-hover:opacity-100 group-focus-visible:opacity-100">
                      g {item.key}
                    </Kbd>
                  </Link>
                </li>
              );
            })}
          </ul>
        </nav>
        <div className="mt-auto border-t border-rule px-2 pt-2 font-mono text-[11px] leading-relaxed text-muted">
          <p>
            <Kbd>/</Kbd> {t.shell.hintSearch} · <Kbd>j</Kbd> <Kbd>k</Kbd> {t.shell.hintMove}
          </p>
          <p className="mt-1">
            <Kbd>Enter</Kbd> {t.shell.hintOpen} · <Kbd>Esc</Kbd> {t.shell.hintClose}
          </p>
        </div>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-20 flex h-12 items-center gap-3 border-b border-rule bg-surface px-3 min-[900px]:px-6">
          <span className="hidden whitespace-nowrap font-mono text-meta text-muted min-[900px]:inline">
            {org && <>{org} / </>}
            <b className="font-medium text-ink">{screenLabel(pathname, t)}</b>
          </span>
          <form
            role="search"
            className="flex h-[30px] min-w-0 max-w-[400px] flex-1 items-center gap-2 rounded-control border border-rule bg-canvas px-2 text-ui text-muted focus-within:border-focus min-[900px]:ml-4"
            onSubmit={(e) => {
              e.preventDefault();
              if (q.trim().length >= 2) router.push(`/search?q=${encodeURIComponent(q.trim())}`);
            }}
          >
            <svg width="13" height="13" viewBox="0 0 16 16" fill="none" aria-hidden="true" className="shrink-0">
              <circle cx="7" cy="7" r="5" stroke="currentColor" strokeWidth="1.6" />
              <path d="M11 11l3.5 3.5" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" />
            </svg>
            <input
              ref={searchRef}
              type="search"
              value={q}
              onChange={(e) => setQ(e.target.value)}
              onKeyDown={(e) => e.key === "Escape" && e.currentTarget.blur()}
              placeholder={t.shell.searchPlaceholder}
              aria-label={t.shell.searchLabel}
              aria-keyshortcuts="/"
              className="w-full min-w-0 bg-transparent text-ink outline-none placeholder:text-muted"
            />
            <Kbd className="hidden sm:inline-block">/</Kbd>
          </form>
          <div className="ml-auto flex items-center gap-2">
            <LangToggle />
            <ThemeToggle />
            {authEnabled ? <AccountMenu /> : <UserPicker />}
          </div>
        </header>
        <nav
          aria-label={t.nav.screens}
          className="flex gap-0.5 overflow-x-auto border-b border-rule bg-surface px-3 py-1.5 [scrollbar-width:none] min-[900px]:hidden"
        >
          {NAV.map((item) => {
            const active = isActive(pathname, item.href);
            return (
              <Link
                key={item.href}
                href={item.href}
                aria-current={active ? "page" : undefined}
                className={`inline-flex h-7 shrink-0 items-center rounded-control px-2.5 text-[13px] ${
                  active ? "bg-sunken font-semibold text-ink" : "text-ink-soft"
                }`}
              >
                {t.nav[item.label]}
              </Link>
            );
          })}
        </nav>
        <main className="w-full max-w-[1280px] flex-1 px-4 pb-12 pt-4 min-[900px]:px-6 min-[900px]:pb-16 min-[900px]:pt-6">{children}</main>
      </div>

      <IssuePanel />
    </div>
  );
}
