"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { ErrorBox, Loading } from "@/components/Feedback";
import { StatusColumns } from "@/components/StatusColumns";
import { useApi } from "@/lib/api";
import { STATUS_LABEL, STATUS_ORDER, formatDate, groupByStatus } from "@/lib/labels";
import type { BoardIssue, MeetingNoteSummary, Project } from "@/lib/types";

export default function ProjectsBoardPage() {
  const router = useRouter();
  const selectedId = useSearchParams().get("project");
  const projects = useApi<Project[]>("/api/projects");
  const issues = useApi<BoardIssue[]>("/api/issues");
  const notes = useApi<MeetingNoteSummary[]>("/api/meeting-notes");

  const error = projects.error ?? issues.error ?? notes.error;
  if (error) return <ErrorBox error={error} />;
  if (!projects.data || !issues.data || !notes.data) return <Loading />;

  const selected = projects.data.find((p) => p.id === selectedId) ?? null;

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-xl font-bold">프로젝트별 보드</h1>
        <p className="text-sm text-slate-500">프로젝트 = 저장소 묶음. 진행률과 최근 회의를 함께 봅니다.</p>
      </div>

      <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3">
        {projects.data.map((p) => {
          const own = issues.data!.filter((i) => i.projectIds.includes(p.id));
          const groups = groupByStatus(own);
          const done = own.filter((i) => i.category === "done").length;
          const pct = own.length ? Math.round((done / own.length) * 100) : 0;
          const recentNotes = notes.data!.filter((n) => n.project?.id === p.id).slice(0, 2);
          const isSelected = p.id === selected?.id;

          return (
            <button
              key={p.id}
              type="button"
              onClick={() => router.push(isSelected ? "/projects" : `/projects?project=${p.id}`, { scroll: false })}
              className={`rounded-xl border p-4 text-left transition ${isSelected ? "border-blue-500 ring-2 ring-blue-200 dark:ring-blue-900" : "border-slate-200 hover:border-blue-300 dark:border-slate-800"}`}
            >
              <div className="flex items-center gap-2">
                <span className="h-3 w-3 rounded-sm" style={{ background: p.color ?? "#94a3b8" }} />
                <h2 className="font-semibold">{p.name}</h2>
                <span className="ml-auto text-xs text-slate-500">{p.repositories.map((r) => r.name).join(", ")}</span>
              </div>
              <div className="mt-3 h-2 overflow-hidden rounded-full bg-slate-100 dark:bg-slate-800">
                <div className="h-full bg-emerald-500" style={{ width: `${pct}%` }} />
              </div>
              <p className="mt-1 text-xs text-slate-500">
                완료 {done} / 전체 {own.length} ({pct}%)
              </p>
              <p className="mt-2 flex flex-wrap gap-x-3 text-xs text-slate-600 dark:text-slate-300">
                {STATUS_ORDER.map((s) => (
                  <span key={s}>
                    {STATUS_LABEL[s]} {groups[s].length}
                  </span>
                ))}
              </p>
              {recentNotes.length > 0 && (
                <ul className="mt-3 border-t border-slate-100 pt-2 text-xs text-slate-500 dark:border-slate-800">
                  {recentNotes.map((n) => (
                    <li key={n.id}>
                      회의: {n.title} · {formatDate(n.heldAt)}
                    </li>
                  ))}
                </ul>
              )}
            </button>
          );
        })}
      </div>

      {selected && (
        <section className="flex flex-col gap-3">
          <div className="flex items-baseline gap-3">
            <h2 className="text-lg font-semibold">{selected.name}</h2>
            <Link href={`/notes?project=${selected.id}`} className="text-sm text-blue-600 hover:underline">
              이 프로젝트의 회의 노트
            </Link>
          </div>
          <StatusColumns issues={issues.data.filter((i) => i.projectIds.includes(selected.id))} />
        </section>
      )}
    </div>
  );
}
