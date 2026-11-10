"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Empty, ErrorBox, Loading } from "@/components/Feedback";
import { useApi } from "@/lib/api";
import { formatDate } from "@/lib/labels";
import type { MeetingNoteSummary } from "@/lib/types";

export default function MeetingNotesPage() {
  const projectId = useSearchParams().get("project");
  const notes = useApi<MeetingNoteSummary[]>(
    projectId ? `/api/meeting-notes?projectId=${encodeURIComponent(projectId)}` : "/api/meeting-notes",
  );

  if (notes.error) return <ErrorBox error={notes.error} />;
  if (!notes.data) return <Loading />;

  return (
    <div className="flex flex-col gap-4">
      <div>
        <h1 className="text-xl font-bold">회의 노트</h1>
        <p className="text-sm text-slate-500">회의에서 나온 할 일은 이슈와 연결되어 이슈 상세에서도 보입니다.</p>
      </div>
      {notes.data.length === 0 ? (
        <Empty>회의 노트가 없습니다.</Empty>
      ) : (
        <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200 dark:divide-slate-800 dark:border-slate-800">
          {notes.data.map((n) => (
            <li key={n.id}>
              <Link href={`/notes/view?id=${n.id}`} className="flex items-center gap-3 px-4 py-3 hover:bg-slate-50 dark:hover:bg-slate-900">
                <div className="min-w-0 flex-1">
                  <p className="font-medium">{n.title}</p>
                  <p className="text-xs text-slate-500">
                    {formatDate(n.heldAt)}
                    {n.project && ` · ${n.project.name}`}
                  </p>
                </div>
                <span className="text-xs text-slate-500">
                  할 일 {n.actionItemCount}건 중 이슈 연결 {n.linkedActionItemCount}건
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
