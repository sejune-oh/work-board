"use client";

import Link from "next/link";
import { useEffect, useRef } from "react";
import { useApi } from "@/lib/api";
import { useFormat, useT } from "@/lib/i18n";
import { daysSince, isStale } from "@/lib/labels";
import { useSettings } from "@/lib/me";
import type { IssueDetail } from "@/lib/types";
import { ErrorBox, Loading } from "./Feedback";
import { Avatar, Count, Flag, Kbd, StatusBadge } from "./ui";
import { useIssuePanel } from "./useIssuePanel";

/** Everything about one issue in one place: PRs, sub-issues, comments and the meetings it came from. */
export function IssuePanel() {
  const { issueId, open, close } = useIssuePanel();
  const { data, error, loading } = useApi<IssueDetail>(issueId ? `/api/issues/${issueId}` : null);
  const settings = useSettings();
  const t = useT();
  const p = t.panel;
  const fmt = useFormat();
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
  const urgent = shown && settings.priorityEnabled && shown.priority === "urgent" && shown.category !== "done";

  return (
    <>
      <div className="fixed inset-0 z-30 bg-scrim" onClick={close} aria-hidden />
      <aside
        className="fixed inset-y-0 right-0 z-40 flex w-full max-w-[560px] flex-col overflow-y-auto bg-surface shadow-panel min-[561px]:rounded-l-sheet"
        aria-label={p.aria}
        role="dialog"
      >
        <div className="sticky top-0 z-10 flex h-14 shrink-0 items-center justify-between gap-3 border-b border-rule bg-surface px-6">
          <span className="font-mono text-meta text-muted">{shown ? `${shown.repository.name} #${shown.number}` : p.issue}</span>
          <button
            ref={closeRef}
            type="button"
            onClick={close}
            aria-keyshortcuts="Escape"
            className="btn h-8 gap-1.5 px-2.5 text-meta font-medium text-ink-soft hover:text-ink"
          >
            {p.close} <Kbd>Esc</Kbd>
          </button>
        </div>

        <div className="flex flex-col gap-7 px-6 py-6 max-[639px]:px-4">
          {loading && !shown && <Loading />}
          {error && <ErrorBox error={error} />}
          {shown && (
            <>
              <header>
                {shown.parent && (
                  <button type="button" onClick={() => open(shown.parent!.id)} className="link mb-2 cursor-pointer text-left text-small text-ink-soft">
                    {p.parent(shown.parent.number, shown.parent.title)}
                  </button>
                )}
                <h2 className="text-title font-semibold">{shown.title}</h2>
                <dl className="mt-5 grid grid-cols-[7rem_minmax(0,1fr)] overflow-hidden rounded-item text-ui shadow-elev-1 [&>dd]:flex [&>dd]:min-h-10 [&>dd]:items-center [&>dd]:border-t [&>dd]:border-rule [&>dd]:px-3 [&>dd]:py-1.5 [&>dt]:flex [&>dt]:items-center [&>dt]:border-t [&>dt]:border-rule [&>dt]:px-3 [&>dt]:py-1.5 [&>dt]:text-small [&>dt:first-of-type]:border-t-0 [&>dd:first-of-type]:border-t-0">
                  <dt className="text-muted">{p.status}</dt>
                  <dd className="flex flex-wrap items-center gap-2">
                    <StatusBadge status={shown.status} />
                    {shown.status === "cancelled" && <span className="text-small text-muted">{t.cancelledHint}</span>}
                    {urgent && <Flag fill>{t.priority.urgent}</Flag>}
                    {isStale(shown) && <Flag>{t.common.stalledFor(daysSince(shown.updatedAt))}</Flag>}
                  </dd>
                  <dt className="text-muted">{p.assignees}</dt>
                  <dd className="flex flex-wrap items-center gap-3">
                    {shown.assignees.length ? (
                      shown.assignees.map((a) => (
                        <span key={a.githubUserId} className="inline-flex items-center gap-1.5">
                          <Avatar person={a} size="sm" />
                          {fmt.person(a)}
                        </span>
                      ))
                    ) : shown.state === "open" ? (
                      <span className="text-accent">{t.common.unassigned}</span>
                    ) : (
                      <span className="text-muted">{t.common.none}</span>
                    )}
                  </dd>
                  {shown.issueType && (
                    <>
                      <dt className="text-muted">{p.type}</dt>
                      <dd>{fmt.issueType(shown.issueType)}</dd>
                    </>
                  )}
                  {settings.sprintsEnabled && shown.sprint && (
                    <>
                      <dt className="text-muted">{p.sprint}</dt>
                      <dd>{shown.sprint.name}</dd>
                    </>
                  )}
                  {settings.priorityEnabled && shown.priority && shown.priority !== "urgent" && (
                    <>
                      <dt className="text-muted">{p.priority}</dt>
                      <dd>{t.priority[shown.priority]}</dd>
                    </>
                  )}
                  {settings.sizeEnabled && shown.size && (
                    <>
                      <dt className="text-muted">{p.size}</dt>
                      <dd>{t.size[shown.size]}</dd>
                    </>
                  )}
                  <dt className="text-muted">{p.lastActivity}</dt>
                  <dd className="text-small">{fmt.relativeDays(shown.updatedAt)}</dd>
                </dl>
              </header>

              {shown.body && <p className="whitespace-pre-wrap text-body text-ink-soft">{shown.body}</p>}

              <Section title={p.pullRequests} count={shown.pullRequests.length}>
                {shown.pullRequests.map((pr) => (
                  <li key={pr.id} className="flex justify-between gap-3 py-2 text-ui">
                    <span className="truncate">
                      <span className="font-mono text-meta text-muted">#{pr.number}</span> {pr.title}
                    </span>
                    <span className="shrink-0 text-small text-muted">
                      {p.prState[pr.state]}
                      {pr.reviewRequested && pr.state === "open" ? p.reviewRequested : ""}
                      {pr.draft ? p.draft : ""}
                    </span>
                  </li>
                ))}
              </Section>

              <Section title={p.children} count={shown.children.length}>
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

              <Section title={p.meetings} count={shown.meetingNotes.length}>
                {shown.meetingNotes.map((n) => (
                  <li key={n.id} className="py-2 text-ui">
                    <Link href={`/notes/view?id=${n.id}`} className="link">
                      {n.title}
                    </Link>{" "}
                    <span className="text-small text-muted">{fmt.date(n.heldAt)}</span>
                  </li>
                ))}
              </Section>

              <Section title={p.conversation} count={shown.comments.length} divided={false}>
                {shown.comments.map((c) => (
                  <li key={c.id} className="border-l-2 border-rule-strong py-0.5 pl-3 text-ui">
                    <div className="mb-1 text-small text-muted">
                      <span className="font-medium text-ink-soft">{fmt.person(c.author)}</span> · <span>{fmt.relativeDays(c.createdAt)}</span>
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
  const t = useT();
  return (
    <section>
      <h3 className="mb-2 flex items-center gap-2 text-heading font-semibold">
        {title} <Count n={count} />
      </h3>
      {count === 0 ? (
        <p className="text-small text-muted">{t.common.none}</p>
      ) : (
        <ul className={divided ? "divide-y divide-rule" : "flex flex-col gap-3"}>{children}</ul>
      )}
    </section>
  );
}
