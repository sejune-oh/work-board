"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { ErrorBox, Loading } from "@/components/Feedback";
import { useIssuePanel } from "@/components/useIssuePanel";
import { Avatar, PageHeader, SectionHead, Stamp, StatusBadge } from "@/components/ui";
import { useApi } from "@/lib/api";
import { formatDate, personName } from "@/lib/labels";
import type { MeetingNote } from "@/lib/types";

export default function MeetingNoteViewPage() {
  const id = useSearchParams().get("id");
  const { open } = useIssuePanel();
  const note = useApi<MeetingNote>(id ? `/api/meeting-notes/${id}` : null);

  if (!id) return <p className="text-ui text-muted">회의 노트를 선택하세요.</p>;
  if (note.error) return <ErrorBox error={note.error} />;
  if (!note.data) return <Loading />;
  const n = note.data;
  const missing = n.actionItems.filter((a) => !a.issue).length;

  return (
    <article className="flex max-w-[820px] flex-col gap-8">
      <Link href="/notes" className="link self-start text-meta text-ink-soft">
        ← 회의 노트 목록
      </Link>
      <PageHeader eyebrow={`${formatDate(n.heldAt)}${n.project ? ` · ${n.project.name}` : ""}`} title={n.title}>
        참석 {n.attendees.length ? n.attendees.map(personName).join(", ") : "기록 없음"}
      </PageHeader>

      <section className="reveal [animation-delay:40ms]">
        <SectionHead
          eyebrow="회의에서 정한 것"
          title={
            <>
              할 일 <span className="font-mono text-meta font-normal text-muted">{n.actionItems.length}</span>
            </>
          }
          action={missing > 0 ? <span className="text-muted">이슈 없음 {missing}건</span> : null}
        />
        {n.actionItems.length === 0 ? (
          <p className="text-meta text-muted">이 회의에서 정한 할 일이 없습니다.</p>
        ) : (
          <ul>
            {n.actionItems.map((a) => (
              <li key={a.id} className="flex flex-col gap-1.5 border-b border-rule py-3 last:border-b-0">
                <span className="text-ui font-medium">{a.text}</span>
                <span className="flex flex-wrap items-center gap-x-3 gap-y-1.5 text-meta text-ink-soft">
                  {a.assignee ? (
                    <span className="inline-flex items-center gap-1.5">
                      <Avatar person={a.assignee} size="sm" />
                      {personName(a.assignee)}
                    </span>
                  ) : (
                    <span className="text-accent">담당자 없음</span>
                  )}
                  {a.issue ? (
                    <button
                      type="button"
                      onClick={() => open(a.issue!.id)}
                      className="inline-flex cursor-pointer items-center gap-2 hover:underline hover:underline-offset-3"
                    >
                      <span className="font-mono">
                        {a.issue.repositoryName} #{a.issue.number}
                      </span>
                      <StatusBadge status={a.issue.status} />
                    </button>
                  ) : (
                    <>
                      <Stamp>이슈 없음</Stamp>
                      <span className="text-muted" title="쓰기 기능이 들어오면 여기서 이슈를 만듭니다">
                        이슈로 만들기 (예정)
                      </span>
                    </>
                  )}
                </span>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="reveal [animation-delay:80ms]">
        <SectionHead eyebrow="기록" title="노트" />
        <pre className="whitespace-pre-wrap break-keep rounded-card border border-rule bg-surface p-4 font-sans text-body text-ink-soft">
          {n.bodyMd}
        </pre>
      </section>
    </article>
  );
}
