"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { ErrorBox, Loading } from "@/components/Feedback";
import { StatusBadge } from "@/components/StatusBadge";
import { useIssuePanel } from "@/components/useIssuePanel";
import { useApi } from "@/lib/api";
import { formatDate, personName } from "@/lib/labels";
import type { MeetingNote } from "@/lib/types";

export default function MeetingNoteViewPage() {
  const id = useSearchParams().get("id");
  const { open } = useIssuePanel();
  const note = useApi<MeetingNote>(id ? `/api/meeting-notes/${id}` : null);

  if (!id) return <p className="text-sm text-slate-500">회의 노트를 선택하세요.</p>;
  if (note.error) return <ErrorBox error={note.error} />;
  if (!note.data) return <Loading />;
  const n = note.data;

  return (
    <article className="flex max-w-3xl flex-col gap-5">
      <Link href="/notes" className="text-sm text-blue-600 hover:underline">
        ← 회의 노트 목록
      </Link>
      <header>
        <h1 className="text-xl font-bold">{n.title}</h1>
        <p className="text-sm text-slate-500">
          {formatDate(n.heldAt)}
          {n.project && ` · ${n.project.name}`} · 참석 {n.attendees.map(personName).join(", ")}
        </p>
      </header>

      <section>
        <h2 className="mb-2 text-sm font-semibold">할 일</h2>
        <ul className="flex flex-col gap-2">
          {n.actionItems.map((a) => (
            <li key={a.id} className="flex items-center gap-3 rounded-lg border border-slate-200 px-3 py-2 text-sm dark:border-slate-800">
              <span className="flex-1">{a.text}</span>
              <span className="text-xs text-slate-500">{a.assignee ? personName(a.assignee) : "담당자 없음"}</span>
              {a.issue ? (
                <button type="button" onClick={() => open(a.issue!.id)} className="flex items-center gap-2 text-xs text-blue-600 hover:underline">
                  {a.issue.repositoryName} #{a.issue.number}
                  <StatusBadge status={a.issue.status} />
                </button>
              ) : (
                <span className="rounded bg-slate-100 px-2 py-0.5 text-xs text-slate-500 dark:bg-slate-800" title="M4에서 구현">
                  이슈로 만들기 (예정)
                </span>
              )}
            </li>
          ))}
        </ul>
      </section>

      <section>
        <h2 className="mb-2 text-sm font-semibold">노트</h2>
        <pre className="whitespace-pre-wrap rounded-lg bg-slate-50 p-4 font-sans text-sm dark:bg-slate-900">{n.bodyMd}</pre>
      </section>
    </article>
  );
}
