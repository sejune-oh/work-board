"use client";

import Link from "next/link";
import { useEffect, useRef } from "react";
import { useApi } from "@/lib/api";
import { PRIORITY_LABEL, SIZE_LABEL, TYPE_LABEL, daysSince, formatDate, isStale, personName, relativeDays } from "@/lib/labels";
import { useSettings } from "@/lib/me";
import type { IssueDetail } from "@/lib/types";
import { ErrorBox, Loading } from "./Feedback";
import { Avatar, Count, Flag, Kbd, StatusBadge } from "./ui";
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
  const settings = useSettings();
  const closeRef = useRef<HTMLButtonElement>(null);

  const isOpen = issueId !== null;
  useEffect(() => {
    if (!isOpen) return;
    const returnTo = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    closeRef.current?.focus();
    return () => returnTo?.focus();
  }, [isOpen]);

  useEffect(() => {
    if (!isOpen) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && close();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [isOpen, close]);

  if (!issueId) return null;
  const shown = data && data.id === issueId ? data : null;
  const urgent = shown && settings.priorityEnabled && shown.priority === "urgent" && shown.status !== "done";

  return (
    <>
      <div className="fixed inset-0 z-30 bg-scrim" onClick={close} aria-hidden />
      <aside
        className="fixed inset-y-0 right-0 z-40 flex w-full max-w-[560px] flex-col overflow-y-auto bg-surface shadow-panel"
        aria-label="이슈 상세"
        role="dialog"
      >
        <div className="sticky top-0 z-10 flex h-12 shrink-0 items-center justify-between gap-3 border-b border-rule bg-surface px-4">
          <span className="font-mono text-meta text-muted">{shown ? `${shown.repository.name} #${shown.number}` : "이슈"}</span>
          <button
            ref={closeRef}
            type="button"
            onClick={close}
            aria-keyshortcuts="Escape"
            className="inline-flex h-7 cursor-pointer items-center gap-1.5 rounded-control border border-rule bg-surface px-2 text-meta text-ink-soft hover:border-rule-strong hover:text-ink"
          >
            닫기 <Kbd>Esc</Kbd>
          </button>
        </div>

        <div className="flex flex-col gap-5 px-4 py-[18px]">
          {loading && !shown && <Loading />}
          {error && <ErrorBox error={error} />}
          {shown && (
            <>
              <header>
                {shown.parent && (
                  <button type="button" onClick={() => open(shown.parent!.id)} className="link mb-1.5 cursor-pointer text-left text-meta text-ink-soft">
                    상위 일 · #{shown.parent.number} {shown.parent.title}
                  </button>
                )}
                <h2 className="text-title font-semibold tracking-[-0.01em]">{shown.title}</h2>
                <dl className="mt-4 grid grid-cols-[6.5rem_minmax(0,1fr)] overflow-hidden rounded-card border border-rule text-ui [&>dd]:flex [&>dd]:min-h-9 [&>dd]:items-center [&>dd]:border-t [&>dd]:border-rule [&>dd]:px-3 [&>dd]:py-1.5 [&>dt]:flex [&>dt]:items-center [&>dt]:border-t [&>dt]:border-rule [&>dt]:px-3 [&>dt]:py-1.5 [&>dt]:text-meta [&>dt:first-of-type]:border-t-0 [&>dd:first-of-type]:border-t-0">
                  <dt className="text-muted">상태</dt>
                  <dd className="flex flex-wrap items-center gap-2">
                    <StatusBadge status={shown.status} />
                    {urgent && <Flag fill>긴급</Flag>}
                    {isStale(shown) && <Flag>{daysSince(shown.updatedAt)}일째 멈춤</Flag>}
                  </dd>
                  <dt className="text-muted">담당자</dt>
                  <dd className="flex flex-wrap items-center gap-3">
                    {shown.assignees.length ? (
                      shown.assignees.map((a) => (
                        <span key={a.githubUserId} className="inline-flex items-center gap-1.5">
                          <Avatar person={a} size="sm" />
                          {personName(a)}
                        </span>
                      ))
                    ) : shown.state === "open" ? (
                      <span className="text-accent">담당자 없음</span>
                    ) : (
                      <span className="text-muted">없음</span>
                    )}
                  </dd>
                  {shown.issueType && (
                    <>
                      <dt className="text-muted">종류</dt>
                      <dd>{TYPE_LABEL[shown.issueType] ?? shown.issueType}</dd>
                    </>
                  )}
                  {settings.sprintsEnabled && shown.sprint && (
                    <>
                      <dt className="text-muted">주기</dt>
                      <dd>{shown.sprint.name}</dd>
                    </>
                  )}
                  {settings.priorityEnabled && shown.priority && shown.priority !== "urgent" && (
                    <>
                      <dt className="text-muted">우선순위</dt>
                      <dd>{PRIORITY_LABEL[shown.priority]}</dd>
                    </>
                  )}
                  {settings.sizeEnabled && shown.size && (
                    <>
                      <dt className="text-muted">크기</dt>
                      <dd>{SIZE_LABEL[shown.size]}</dd>
                    </>
                  )}
                  <dt className="text-muted">마지막 활동</dt>
                  <dd className="font-mono text-meta">{relativeDays(shown.updatedAt)}</dd>
                </dl>
              </header>

              {shown.body && <p className="whitespace-pre-wrap text-body text-ink-soft">{shown.body}</p>}

              <Section title="연결된 PR" count={shown.pullRequests.length}>
                {shown.pullRequests.map((pr) => (
                  <li key={pr.id} className="flex justify-between gap-3 py-2 text-ui">
                    <span className="truncate">
                      <span className="font-mono text-meta text-muted">#{pr.number}</span> {pr.title}
                    </span>
                    <span className="shrink-0 text-meta text-muted">
                      {PR_STATE[pr.state]}
                      {pr.reviewRequested && pr.state === "open" ? " · 리뷰 요청" : ""}
                      {pr.draft ? " · 초안" : ""}
                    </span>
                  </li>
                ))}
              </Section>

              <Section title="하위 일" count={shown.children.length}>
                {shown.children.map((c) => (
                  <li key={c.id}>
                    <button
                      type="button"
                      onClick={() => open(c.id)}
                      className="-mx-1.5 flex w-[calc(100%+0.75rem)] cursor-pointer items-center justify-between gap-3 rounded-control px-1.5 py-2 text-left text-ui hover:bg-hover"
                    >
                      <span className="truncate">
                        <span className="font-mono text-meta text-muted">
                          {c.repositoryName} #{c.number}
                        </span>{" "}
                        {c.title}
                      </span>
                      <StatusBadge status={c.status} />
                    </button>
                  </li>
                ))}
              </Section>

              <Section title="이 일이 나온 회의" count={shown.meetingNotes.length}>
                {shown.meetingNotes.map((n) => (
                  <li key={n.id} className="py-2 text-ui">
                    <Link href={`/notes/view?id=${n.id}`} className="link">
                      {n.title}
                    </Link>{" "}
                    <span className="font-mono text-meta text-muted">{formatDate(n.heldAt)}</span>
                  </li>
                ))}
              </Section>

              <Section title="대화" count={shown.comments.length} divided={false}>
                {shown.comments.map((c) => (
                  <li key={c.id} className="border-l-2 border-rule-strong py-0.5 pl-3 text-ui">
                    <div className="mb-1 text-meta text-muted">
                      <span className="font-medium text-ink-soft">{personName(c.author)}</span> · <span className="font-mono">{relativeDays(c.createdAt)}</span>
                    </div>
                    <p className="whitespace-pre-wrap text-ink-soft">{c.body}</p>
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

function Section({ title, count, divided = true, children }: { title: string; count: number; divided?: boolean; children: React.ReactNode }) {
  return (
    <section>
      <h3 className="mb-1 flex items-center gap-2 border-b border-rule pb-1.5 text-heading font-semibold">
        {title} <Count n={count} />
      </h3>
      {count === 0 ? (
        <p className="text-meta text-muted">없음</p>
      ) : (
        <ul className={divided ? "divide-y divide-rule" : "flex flex-col gap-3"}>{children}</ul>
      )}
    </section>
  );
}
