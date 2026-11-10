"use client";

import Link from "next/link";
import { useEffect } from "react";
import { useApi } from "@/lib/api";
import { PRIORITY_LABEL, SIZE_LABEL, TYPE_LABEL, formatDate, personName, relativeDays } from "@/lib/labels";
import type { IssueDetail } from "@/lib/types";
import { ErrorBox, Loading } from "./Feedback";
import { StatusBadge } from "./StatusBadge";
import { useIssuePanel } from "./useIssuePanel";

const PR_STATE: Record<IssueDetail["pullRequests"][number]["state"], string> = {
  open: "열림",
  closed: "닫힘",
  merged: "병합됨",
};

/** Everything about one issue in one place: PRs, sub-issues, comments and the meetings it came from. */
export function IssuePanel() {
  const { issueId, open, close } = useIssuePanel();
  const { data, error, loading } = useApi<IssueDetail>(issueId ? `/api/issues/${issueId}` : null);

  useEffect(() => {
    if (!issueId) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && close();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [issueId, close]);

  if (!issueId) return null;

  return (
    <>
      <div className="fixed inset-0 z-30 bg-black/20" onClick={close} aria-hidden />
      <aside
        className="fixed inset-y-0 right-0 z-40 flex w-full max-w-xl flex-col overflow-y-auto border-l border-slate-200 bg-white shadow-xl dark:border-slate-800 dark:bg-slate-950"
        aria-label="이슈 상세"
      >
        <div className="sticky top-0 flex items-center justify-between border-b border-slate-200 bg-white px-5 py-3 dark:border-slate-800 dark:bg-slate-950">
          <span className="text-sm text-slate-500">
            {data ? `${data.repository.name} #${data.number}` : "이슈"}
          </span>
          <button type="button" onClick={close} className="rounded px-2 py-1 text-sm text-slate-500 hover:bg-slate-100 dark:hover:bg-slate-800">
            닫기 ✕
          </button>
        </div>

        <div className="flex flex-col gap-6 px-5 py-4">
          {loading && !data && <Loading />}
          {error && <ErrorBox error={error} />}
          {data && (
            <>
              <header>
                {data.parent && (
                  <button type="button" onClick={() => open(data.parent!.id)} className="mb-1 text-xs text-blue-600 hover:underline">
                    상위 일: #{data.parent.number} {data.parent.title}
                  </button>
                )}
                <h2 className="text-lg font-semibold text-slate-900 dark:text-slate-50">{data.title}</h2>
                <dl className="mt-3 grid grid-cols-[6rem_1fr] gap-y-1.5 text-sm">
                  <dt className="text-slate-500">상태</dt>
                  <dd><StatusBadge status={data.status} /></dd>
                  <dt className="text-slate-500">담당자</dt>
                  <dd>{data.assignees.length ? data.assignees.map(personName).join(", ") : "없음"}</dd>
                  {data.issueType && (<><dt className="text-slate-500">종류</dt><dd>{TYPE_LABEL[data.issueType] ?? data.issueType}</dd></>)}
                  {data.sprint && (<><dt className="text-slate-500">주기</dt><dd>{data.sprint.name}</dd></>)}
                  {data.priority && (<><dt className="text-slate-500">우선순위</dt><dd>{PRIORITY_LABEL[data.priority]}</dd></>)}
                  {data.size && (<><dt className="text-slate-500">크기</dt><dd>{SIZE_LABEL[data.size]}</dd></>)}
                  <dt className="text-slate-500">마지막 활동</dt>
                  <dd>{relativeDays(data.updatedAt)}</dd>
                </dl>
              </header>

              {data.body && <p className="whitespace-pre-wrap text-sm text-slate-700 dark:text-slate-300">{data.body}</p>}

              <Section title="연결된 PR" count={data.pullRequests.length}>
                {data.pullRequests.map((pr) => (
                  <li key={pr.id} className="flex justify-between gap-2 text-sm">
                    <span className="truncate">#{pr.number} {pr.title}</span>
                    <span className="shrink-0 text-xs text-slate-500">
                      {PR_STATE[pr.state]}{pr.reviewRequested && pr.state === "open" ? " · 리뷰 요청" : ""}{pr.draft ? " · 초안" : ""}
                    </span>
                  </li>
                ))}
              </Section>

              <Section title="하위 일" count={data.children.length}>
                {data.children.map((c) => (
                  <li key={c.id}>
                    <button type="button" onClick={() => open(c.id)} className="flex w-full justify-between gap-2 text-left text-sm hover:text-blue-600">
                      <span className="truncate">{c.repositoryName} #{c.number} {c.title}</span>
                      <StatusBadge status={c.status} />
                    </button>
                  </li>
                ))}
              </Section>

              <Section title="이 일이 나온 회의" count={data.meetingNotes.length}>
                {data.meetingNotes.map((n) => (
                  <li key={n.id} className="text-sm">
                    <Link href={`/notes/view?id=${n.id}`} className="hover:text-blue-600">
                      {n.title} <span className="text-xs text-slate-500">{formatDate(n.heldAt)}</span>
                    </Link>
                  </li>
                ))}
              </Section>

              <Section title="대화" count={data.comments.length}>
                {data.comments.map((c) => (
                  <li key={c.id} className="rounded-lg bg-slate-50 px-3 py-2 text-sm dark:bg-slate-900">
                    <div className="mb-1 text-xs text-slate-500">
                      {personName(c.author)} · {relativeDays(c.createdAt)}
                    </div>
                    <p className="whitespace-pre-wrap">{c.body}</p>
                  </li>
                ))}
              </Section>
            </>
          )}
        </div>
      </aside>
    </>
  );
}

function Section({ title, count, children }: { title: string; count: number; children: React.ReactNode }) {
  return (
    <section>
      <h3 className="mb-2 text-sm font-semibold text-slate-700 dark:text-slate-200">
        {title} <span className="font-normal text-slate-400">{count}</span>
      </h3>
      {count === 0 ? <p className="text-sm text-slate-400">없음</p> : <ul className="flex flex-col gap-2">{children}</ul>}
    </section>
  );
}
