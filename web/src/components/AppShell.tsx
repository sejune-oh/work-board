"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError } from "@/lib/api";
import { useMe } from "@/lib/me";
import { ErrorBox, Loading } from "./Feedback";
import { IssuePanel } from "./IssuePanel";
import { StartScreen } from "./StartScreen";
import { UserPicker } from "./UserPicker";

const NAV = [
  { href: "/", label: "내 작업" },
  { href: "/people", label: "사람별 보드" },
  { href: "/projects", label: "프로젝트별 보드" },
  { href: "/notes", label: "회의 노트" },
  { href: "/settings", label: "설정" },
];

export function AppShell({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const router = useRouter();
  const [q, setQ] = useState("");
  const me = useMe();
  const needsUser = me.error instanceof ApiError && me.error.status === 401;

  let content: React.ReactNode;
  if (needsUser) content = <StartScreen />;
  else if (me.error) content = <ErrorBox error={me.error} />;
  else if (!me.data) content = <Loading />;
  else content = children;

  return (
    <div className="flex min-h-screen">
      <nav className="hidden w-52 shrink-0 border-r border-slate-200 px-3 py-5 md:block dark:border-slate-800">
        <p className="mb-6 px-2 text-base font-bold">업무 보드</p>
        <ul className="flex flex-col gap-1">
          {NAV.map((item) => {
            const active = item.href === "/" ? pathname === "/" : pathname.startsWith(item.href);
            return (
              <li key={item.href}>
                <Link
                  href={item.href}
                  className={`block rounded-md px-2 py-1.5 text-sm ${active ? "bg-slate-100 font-semibold dark:bg-slate-800" : "text-slate-600 hover:bg-slate-50 dark:text-slate-300 dark:hover:bg-slate-900"}`}
                >
                  {item.label}
                </Link>
              </li>
            );
          })}
        </ul>
      </nav>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex flex-wrap items-center gap-3 border-b border-slate-200 px-4 py-3 md:px-6 dark:border-slate-800">
          <form
            className="flex-1"
            onSubmit={(e) => {
              e.preventDefault();
              if (q.trim().length >= 2) router.push(`/search?q=${encodeURIComponent(q.trim())}`);
            }}
          >
            <input
              value={q}
              onChange={(e) => setQ(e.target.value)}
              placeholder="이슈, 댓글, 회의 노트 검색"
              className="w-full max-w-md rounded-md border border-slate-300 bg-white px-3 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-900"
            />
          </form>
          {!needsUser && <UserPicker />}
        </header>
        <nav className="flex gap-1 overflow-x-auto border-b border-slate-200 px-4 py-2 md:hidden dark:border-slate-800">
          {NAV.map((item) => (
            <Link key={item.href} href={item.href} className="shrink-0 rounded-md px-2 py-1 text-sm text-slate-600 dark:text-slate-300">
              {item.label}
            </Link>
          ))}
        </nav>
        <main className="flex-1 px-4 py-6 md:px-6">{content}</main>
      </div>

      {me.data && !me.error && <IssuePanel />}
    </div>
  );
}
