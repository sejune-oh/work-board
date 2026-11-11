"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useCallback, useEffect, useRef, useState } from "react";
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

const ICONS: Record<keyof Dict["nav"], React.ReactNode> = {
  overview: (
    <>
      <rect x="2" y="2" width="5" height="5" rx="1" />
      <rect x="9" y="2" width="5" height="5" rx="1" />
      <rect x="2" y="9" width="5" height="5" rx="1" />
      <rect x="9" y="9" width="5" height="5" rx="1" />
    </>
  ),
  me: (
    <>
      <circle cx="8" cy="5.5" r="2.5" />
      <path d="M3 13.5c.8-2.3 2.7-3.5 5-3.5s4.2 1.2 5 3.5" />
    </>
  ),
  people: (
    <>
      <circle cx="5.5" cy="6" r="2" />
      <circle cx="11" cy="6" r="2" />
      <path d="M2 13c.5-1.8 1.8-3 3.5-3s3 1.2 3.5 3M8.5 13c.4-1.8 1-3 2.5-3s2.6 1.2 3 3" />
    </>
  ),
  projects: (
    <>
      <rect x="2" y="2.5" width="3.5" height="11" rx="1" />
      <rect x="6.25" y="2.5" width="3.5" height="7" rx="1" />
      <rect x="10.5" y="2.5" width="3.5" height="9" rx="1" />
    </>
  ),
  notes: (
    <>
      <path d="M4 2h6l3 3v9H4z" />
      <path d="M6.5 8h4M6.5 10.5h4" />
    </>
  ),
  settings: (
    <>
      <circle cx="8" cy="8" r="2" />
      <path d="M8 1.5v2M8 12.5v2M1.5 8h2M12.5 8h2M3.4 3.4l1.4 1.4M11.2 11.2l1.4 1.4M3.4 12.6l1.4-1.4M11.2 4.8l1.4-1.4" />
    </>
  ),
  search: null,
  screens: null,
};

const NAV: { href: string; label: keyof Dict["nav"]; key: string }[] = [
  { href: "/", label: "overview", key: "o" },
  { href: "/me", label: "me", key: "m" },
  { href: "/people", label: "people", key: "p" },
  { href: "/projects", label: "projects", key: "b" },
  { href: "/notes", label: "notes", key: "n" },
  { href: "/settings", label: "settings", key: "s" },
];

/** Screens reached from another one count as that one in the menu. */
const PARENT: [prefix: string, href: string][] = [["/connect/", "/settings"]];

function isActive(path: string, href: string) {
  const pathname = PARENT.find(([prefix]) => path.startsWith(prefix))?.[1] ?? path;
  return href === "/" ? pathname === "/" : pathname === href || pathname.startsWith(`${href}/`);
}

function screenLabel(pathname: string, t: Dict) {
  if (pathname.startsWith("/search")) return t.nav.search;
  const item = NAV.find((n) => isActive(pathname, n.href));
  return item ? t.nav[item.label] : t.app.title;
}

function SearchIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden className="shrink-0">
      <circle cx="7" cy="7" r="4.5" />
      <path d="m10.5 10.5 3 3" strokeLinecap="round" />
    </svg>
  );
}

function OrgMark({ org, size }: { org: string; size: "sm" | "md" }) {
  return (
    <span
      aria-hidden
      className={`grid shrink-0 place-items-center rounded-control bg-ink font-mono text-meta font-semibold text-on-ink ${size === "md" ? "h-8 w-8" : "h-7 w-7"}`}
    >
      {org.slice(0, 2) || "wb"}
    </span>
  );
}

/** Marks which edges of the tab strip still have tabs past them (`data-more`), so only those edges fade. */
function useTabFade() {
  const ref = useRef<HTMLElement>(null);
  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    const update = () => {
      const start = el.scrollLeft > 1;
      const end = el.scrollLeft + el.clientWidth < el.scrollWidth - 1;
      const more = start && end ? "both" : start ? "start" : end ? "end" : null;
      if (more) el.dataset.more = more;
      else delete el.dataset.more;
    };
    update();
    el.addEventListener("scroll", update, { passive: true });
    const ro = new ResizeObserver(update);
    ro.observe(el);
    return () => {
      el.removeEventListener("scroll", update);
      ro.disconnect();
    };
  }, []);
  return ref;
}

/**
 * The app chrome. Only the sign-in gate renders it, once `/api/me` has succeeded.
 * From 1024px a full-height sidebar; below that a top bar with tabs that scroll on their own.
 */
export function AppShell({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const router = useRouter();
  const [q, setQ] = useState("");
  const [searchOpen, setSearchOpen] = useState(false);
  const searchRef = useRef<HTMLInputElement>(null);
  const org = useMe().data?.tenant.name ?? "";
  const t = useT();

  /** On phones the field is hidden until opened, so it is shown first and focused after the next paint. */
  const openSearch = useCallback(() => {
    setSearchOpen(true);
    requestAnimationFrame(() => searchRef.current?.focus());
  }, []);

  useShortcuts(openSearch);
  const tabsRef = useTabFade();

  return (
    <div className="flex min-h-dvh">
      <div className="hidden w-[248px] shrink-0 border-r border-rule min-[1024px]:block">
        <aside className="sticky top-0 flex h-dvh flex-col gap-6 overflow-y-auto px-4 py-5">
          <Link href="/" className="flex items-center gap-2.5 rounded-control px-2 py-1.5 hover:bg-hover">
            <OrgMark org={org} size="md" />
            <span className="min-w-0">
              <span className="block truncate text-ui font-semibold">{org}</span>
              <span className="block text-meta text-muted">{t.app.title}</span>
            </span>
          </Link>
          <nav aria-label={t.nav.screens}>
            <ul className="flex flex-col gap-0.5">
              {NAV.map((item) => {
                const active = isActive(pathname, item.href);
                return (
                  <li key={item.href}>
                    <Link
                      href={item.href}
                      aria-current={active ? "page" : undefined}
                      aria-keyshortcuts={`g ${item.key}`}
                      className={`group flex h-9 items-center gap-2.5 rounded-control px-2.5 text-ui transition-colors duration-[120ms] ease-tool ${
                        active ? "bg-surface font-semibold text-ink shadow-elev-1" : "font-medium text-ink-soft hover:bg-hover hover:text-ink"
                      }`}
                    >
                      <svg
                        width="16"
                        height="16"
                        viewBox="0 0 16 16"
                        fill="none"
                        stroke="currentColor"
                        strokeWidth="1.5"
                        aria-hidden
                        className={`shrink-0 ${active ? "text-ink" : "text-muted"}`}
                      >
                        {ICONS[item.label]}
                      </svg>
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
          <div className="mt-auto flex flex-col gap-3">
            <div className="flex flex-wrap gap-2">
              <LangToggle />
              <ThemeToggle />
            </div>
            <p className="flex flex-wrap items-center gap-x-2.5 gap-y-1.5 text-meta leading-5 text-muted">
              <span className="inline-flex items-center gap-1">
                <Kbd>/</Kbd> {t.shell.hintSearch}
              </span>
              <span className="inline-flex items-center gap-1">
                <Kbd>j</Kbd>
                <Kbd>k</Kbd> {t.shell.hintMove}
              </span>
              <span className="inline-flex items-center gap-1">
                <Kbd>Enter</Kbd> {t.shell.hintOpen}
              </span>
              <span className="inline-flex items-center gap-1">
                <Kbd>Esc</Kbd> {t.shell.hintClose}
              </span>
            </p>
          </div>
        </aside>
      </div>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-20 border-b border-rule bg-(--wb-topbar) px-4 backdrop-blur-md backdrop-saturate-150 min-[640px]:px-6 min-[1024px]:px-10">
          <div className="flex h-14 items-center gap-3 min-[1024px]:h-[60px]">
            <Link href="/" className={`flex min-w-0 items-center gap-2.5 min-[1024px]:hidden ${searchOpen ? "max-[639px]:hidden" : ""}`}>
              <OrgMark org={org} size="sm" />
              <span className="truncate text-ui font-semibold max-[639px]:sr-only">{org}</span>
            </Link>
            <p className="hidden min-w-0 items-center gap-2 whitespace-nowrap text-small text-muted min-[1024px]:flex">
              {org && (
                <>
                  <span className="truncate">{org}</span>
                  <span aria-hidden>/</span>
                </>
              )}
              <b className="font-semibold text-ink">{screenLabel(pathname, t)}</b>
            </p>
            <form
              id="site-search"
              role="search"
              className={`ml-auto h-9 min-w-0 flex-1 items-center gap-2 rounded-control bg-surface pl-3 pr-1.5 text-small text-muted shadow-elev-1 focus-within:shadow-[0_0_0_2px_var(--wb-focus)] min-[640px]:flex min-[1024px]:max-w-[340px] ${
                searchOpen ? "flex" : "hidden"
              }`}
              onSubmit={(e) => {
                e.preventDefault();
                if (q.trim().length >= 2) router.push(`/search?q=${encodeURIComponent(q.trim())}`);
              }}
            >
              <SearchIcon />
              <input
                ref={searchRef}
                type="search"
                value={q}
                onChange={(e) => setQ(e.target.value)}
                onKeyDown={(e) => e.key === "Escape" && e.currentTarget.blur()}
                onBlur={() => q === "" && setSearchOpen(false)}
                placeholder={t.shell.searchPlaceholder}
                aria-label={t.shell.searchLabel}
                aria-keyshortcuts="/"
                className="w-full min-w-0 bg-transparent text-ink outline-none placeholder:text-muted"
              />
              <Kbd className="max-[639px]:hidden">/</Kbd>
            </form>
            <button
              type="button"
              onClick={openSearch}
              aria-label={t.shell.searchLabel}
              aria-expanded={searchOpen}
              aria-controls="site-search"
              className={`ml-auto h-9 w-9 shrink-0 cursor-pointer place-items-center rounded-control text-ink-soft hover:bg-hover min-[640px]:hidden ${searchOpen ? "hidden" : "grid"}`}
            >
              <SearchIcon size={18} />
            </button>
            <div className={`flex shrink-0 items-center gap-2 ${searchOpen ? "max-[639px]:hidden" : ""}`}>
              <div className="flex items-center gap-2 min-[1024px]:hidden">
                <LangToggle />
                <ThemeToggle />
              </div>
              {authEnabled ? <AccountMenu /> : <UserPicker />}
            </div>
          </div>
          <nav ref={tabsRef} aria-label={t.nav.screens} className="tabs -mx-4 flex gap-5 overflow-x-auto px-4 min-[640px]:-mx-6 min-[640px]:px-6 min-[1024px]:hidden">
            {NAV.map((item) => {
              const active = isActive(pathname, item.href);
              return (
                <Link
                  key={item.href}
                  href={item.href}
                  aria-current={active ? "page" : undefined}
                  className={`shrink-0 pb-3 pt-2 text-ui ${
                    active ? "font-semibold text-ink shadow-[inset_0_-2px_0_var(--wb-ink)]" : "font-medium text-muted hover:text-ink"
                  }`}
                >
                  {t.nav[item.label]}
                </Link>
              );
            })}
          </nav>
        </header>
        <main className="mx-auto w-full max-w-[1200px] flex-1 px-4 pb-20 pt-6 min-[640px]:px-6 min-[1024px]:px-10 min-[1024px]:pt-8">{children}</main>
      </div>

      <IssuePanel />
    </div>
  );
}
