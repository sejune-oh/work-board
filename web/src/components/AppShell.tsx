"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { ApiError } from "@/lib/api";
import { useMe } from "@/lib/me";
import { ErrorBox, Loading } from "./Feedback";
import { IssuePanel } from "./IssuePanel";
import { StartScreen } from "./StartScreen";
import { ThemeToggle } from "./ThemeToggle";
import { UserPicker } from "./UserPicker";

const NAV = [
  { href: "/", label: "개요" },
  { href: "/me", label: "내 작업" },
  { href: "/people", label: "사람별 보드" },
  { href: "/projects", label: "프로젝트별 보드" },
  { href: "/notes", label: "회의 노트" },
  { href: "/settings", label: "설정" },
];

function isActive(pathname: string, href: string) {
  return href === "/" ? pathname === "/" : pathname === href || pathname.startsWith(`${href}/`);
}

export function AppShell({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const router = useRouter();
  const [q, setQ] = useState("");
  const searchRef = useRef<HTMLInputElement>(null);
  const me = useMe();
  const authError = me.error instanceof ApiError && me.error.status === 401 ? me.error : null;
  const needsUser = authError !== null;

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      const typing = target && (target.tagName === "INPUT" || target.tagName === "TEXTAREA" || target.tagName === "SELECT" || target.isContentEditable);
      if (e.key === "/" && !typing) {
        e.preventDefault();
        searchRef.current?.focus();
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  let content: React.ReactNode;
  if (authError) content = <StartScreen tenantRequired={authError.code === "tenant_required"} />;
  else if (me.error) content = <ErrorBox error={me.error} />;
  else if (!me.data) content = <Loading />;
  else content = children;

  return (
    <div className="relative z-[1] flex min-h-screen">
      <aside className="sticky top-0 hidden h-screen w-[216px] shrink-0 flex-col gap-7 border-r border-rule px-3.5 py-[22px] min-[900px]:flex">
        <Link href="/" className="flex items-center gap-2.5 px-2">
          <span className="grid h-7 w-7 place-items-center rounded-mark bg-ink font-display text-[15px] font-bold text-paper">업</span>
          <span className="font-display text-[17px] font-semibold tracking-[-0.01em]">업무 보드</span>
        </Link>
        <nav aria-label="화면">
          <ul className="flex flex-col gap-0.5">
            {NAV.map((item) => {
              const active = isActive(pathname, item.href);
              return (
                <li key={item.href}>
                  <Link
                    href={item.href}
                    aria-current={active ? "page" : undefined}
                    className={`block rounded-card px-2.5 py-[7px] text-ui transition-colors duration-[120ms] ease-paper ${
                      active ? "bg-surface font-semibold text-ink shadow-[inset_2px_0_0_var(--wb-ink)]" : "font-medium text-ink-soft hover:bg-sunken"
                    }`}
                  >
                    {item.label}
                  </Link>
                </li>
              );
            })}
          </ul>
        </nav>
        {me.data && (
          <div className="mt-auto px-2 text-meta leading-normal text-muted">
            <strong className="block font-mono font-medium text-ink-soft">{me.data.tenant.githubOrgLogin}</strong>
            GitHub 조직
          </div>
        )}
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex flex-wrap items-center gap-3 border-b border-rule px-4 py-2.5 min-[900px]:px-8 min-[900px]:py-3">
          <form
            role="search"
            className="order-last flex basis-full items-center min-[640px]:order-none min-[640px]:max-w-[420px] min-[640px]:flex-1 min-[640px]:basis-auto gap-2 rounded-card border border-rule bg-surface px-2.5 py-1.5 text-ui text-muted focus-within:border-ink-soft"
            onSubmit={(e) => {
              e.preventDefault();
              if (q.trim().length >= 2) router.push(`/search?q=${encodeURIComponent(q.trim())}`);
            }}
          >
            <svg width="14" height="14" viewBox="0 0 16 16" fill="none" aria-hidden="true">
              <circle cx="7" cy="7" r="5" stroke="currentColor" strokeWidth="1.6" />
              <path d="M11 11l3.5 3.5" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" />
            </svg>
            <input
              ref={searchRef}
              type="search"
              value={q}
              onChange={(e) => setQ(e.target.value)}
              placeholder="이슈, 댓글, 회의 노트 검색"
              aria-label="검색"
              className="w-full min-w-0 bg-transparent text-ink outline-none placeholder:text-muted"
            />
            <kbd className="hidden rounded-mark border border-rule px-1.5 font-mono text-[11px] sm:inline">/</kbd>
          </form>
          <div className="ml-auto flex items-center gap-2.5">
            <ThemeToggle />
            {!needsUser && <UserPicker />}
          </div>
        </header>
        <nav aria-label="화면" className="flex gap-1 overflow-x-auto border-b border-rule px-3 py-2 [scrollbar-width:none] min-[900px]:hidden">
          {NAV.map((item) => {
            const active = isActive(pathname, item.href);
            return (
              <Link
                key={item.href}
                href={item.href}
                aria-current={active ? "page" : undefined}
                className={`shrink-0 rounded-card px-2.5 py-1 text-[13.5px] ${active ? "bg-ink font-semibold text-paper" : "text-ink-soft"}`}
              >
                {item.label}
              </Link>
            );
          })}
        </nav>
        <main className="w-full max-w-[1320px] flex-1 px-4 pb-14 pt-6 min-[900px]:px-8 min-[900px]:pb-[72px] min-[900px]:pt-9">{content}</main>
      </div>

      {me.data && !me.error && <IssuePanel />}
    </div>
  );
}
