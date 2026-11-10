"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { ErrorBox, Loading } from "@/components/Feedback";
import { useIssuePanel } from "@/components/useIssuePanel";
import { Avatar, Card, Flag, PageHeader, StatusBadge } from "@/components/ui";
import { useApi } from "@/lib/api";
import { useFormat, useT } from "@/lib/i18n";
import type { MeetingNote } from "@/lib/types";

export default function MeetingNoteViewPage() {
  const id = useSearchParams().get("id");
  const { open } = useIssuePanel();
  const t = useT();
  const nt = t.notes;
  const fmt = useFormat();
  const note = useApi<MeetingNote>(id ? `/api/meeting-notes/${id}` : null);

  if (!id) return <p className="text-ui text-muted">{nt.pick}</p>;
  if (note.error) return <ErrorBox error={note.error} />;
  if (!note.data) return <Loading />;
  const n = note.data;
  const missing = n.actionItems.filter((a) => !a.issue).length;

  return (
    <article className="flex max-w-[820px] flex-col gap-4">
      <Link href="/notes" className="link self-start text-meta text-ink-soft">
        {nt.back}
      </Link>
      <PageHeader meta={`${fmt.date(n.heldAt)}${n.project ? ` · ${n.project.name}` : ""}`} title={n.title}>
        {nt.attendees(n.attendees.length ? n.attendees.map(fmt.person).join(", ") : nt.noAttendees)}
      </PageHeader>

      <Card
        title={nt.actionItems}
        count={n.actionItems.length}
        hint={nt.actionHint}
        action={missing > 0 ? <span>{nt.missingCount(missing)}</span> : null}
        className="reveal mt-2 [animation-delay:30ms]"
      >
        {n.actionItems.length === 0 ? (
          <p className="px-3.5 py-2.5 text-meta text-muted">{nt.noActionItems}</p>
        ) : (
          <ul>
            {n.actionItems.map((a) => (
              <li key={a.id} className="flex flex-col gap-1 border-b border-rule px-3.5 py-2.5 last:border-b-0">
                <span className="text-ui font-medium">{a.text}</span>
                <span className="flex flex-wrap items-center gap-x-3 gap-y-1.5 text-meta text-ink-soft">
                  {a.assignee ? (
                    <span className="inline-flex items-center gap-1.5">
                      <Avatar person={a.assignee} size="sm" />
                      {fmt.person(a.assignee)}
                    </span>
                  ) : (
                    <span className="text-accent">{t.common.unassigned}</span>
                  )}
                  {a.issue ? (
                    <button
                      type="button"
                      onClick={() => open(a.issue!.id)}
                      className="inline-flex cursor-pointer items-center gap-2 [&>.font-mono]:hover:underline [&>.font-mono]:hover:underline-offset-3"
                    >
                      <span className="font-mono">
                        {a.issue.repositoryName} #{a.issue.number}
                      </span>
                      <StatusBadge status={a.issue.status} />
                    </button>
                  ) : (
                    <>
                      <Flag>{t.common.noIssue}</Flag>
                      <span className="text-muted" title={nt.createIssueTitle}>
                        {nt.createIssue}
                      </span>
                    </>
                  )}
                </span>
              </li>
            ))}
          </ul>
        )}
      </Card>

      <Card title={nt.body} hint={nt.bodyHint} className="reveal [animation-delay:60ms]">
        <pre className="whitespace-pre-wrap break-keep p-4 font-sans text-body text-ink-soft">{n.bodyMd}</pre>
      </Card>
    </article>
  );
}
