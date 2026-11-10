"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Empty, ErrorBox, Loading } from "@/components/Feedback";
import { Flag, PageHeader } from "@/components/ui";
import { ROW_ATTR } from "@/components/useShortcuts";
import { useApi } from "@/lib/api";
import { formatDate } from "@/lib/labels";
import type { MeetingNoteSummary } from "@/lib/types";

export default function MeetingNotesPage() {
  const projectId = useSearchParams().get("project");
  const notes = useApi<MeetingNoteSummary[]>(
    projectId ? `/api/meeting-notes?projectId=${encodeURIComponent(projectId)}` : "/api/meeting-notes",
  );

  const list = notes.data ?? [];
  const unlinked = list.reduce((sum, n) => sum + (n.actionItemCount - n.linkedActionItemCount), 0);
  const projectName = projectId ? list.find((n) => n.project?.id === projectId)?.project?.name : null;

  return (
    <div className="flex max-w-[900px] flex-col gap-4">
      <PageHeader meta={projectName ? `회의 노트 · ${projectName}` : "기록"} title="회의 노트">
        {notes.data
          ? unlinked > 0
            ? `회의에서 정한 할 일 중 ${unlinked}건이 아직 이슈로 이어지지 않았습니다.`
            : "회의에서 정한 할 일은 모두 이슈로 이어져 있습니다."
          : "회의에서 나온 할 일은 이슈와 연결되어 이슈 상세에서도 보입니다."}
      </PageHeader>
      {projectId && (
        <Link href="/notes" className="link -mt-2 self-start text-meta text-ink-soft">
          모든 프로젝트의 회의 보기
        </Link>
      )}
      {notes.error ? (
        <ErrorBox error={notes.error} />
      ) : !notes.data ? (
        <Loading />
      ) : list.length === 0 ? (
        <Empty>회의 노트가 없습니다.</Empty>
      ) : (
        <ul className="card reveal [animation-delay:30ms]">
          {list.map((n) => {
            const missing = n.actionItemCount - n.linkedActionItemCount;
            return (
              <li key={n.id} className="border-b border-rule last:border-b-0">
                <Link
                  href={`/notes/view?id=${n.id}`}
                  {...{ [ROW_ATTR]: "" }}
                  className="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-4 gap-y-1 px-3.5 py-2.5 transition-colors duration-[120ms] ease-tool hover:bg-hover focus-visible:bg-hover focus-visible:-outline-offset-2 max-[640px]:grid-cols-1"
                >
                  <div className="min-w-0">
                    <p className="truncate text-ui font-medium">{n.title}</p>
                    <p className="font-mono text-meta text-muted">
                      {formatDate(n.heldAt)}
                      {n.project && <span className="font-sans"> · {n.project.name}</span>}
                    </p>
                  </div>
                  <div className="flex items-center gap-2 text-meta text-ink-soft">
                    {n.actionItemCount === 0 ? (
                      <span className="text-muted">할 일 없음</span>
                    ) : (
                      <span>
                        할 일 <b className="font-mono font-medium">{n.actionItemCount}</b> · 이슈 연결{" "}
                        <b className="font-mono font-medium">{n.linkedActionItemCount}</b>
                      </span>
                    )}
                    {missing > 0 && <Flag>이슈 없음 {missing}</Flag>}
                  </div>
                </Link>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
