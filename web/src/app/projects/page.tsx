"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Empty, ErrorBox, Loading } from "@/components/Feedback";
import { FilterChips } from "@/components/FilterChips";
import { StatusColumns } from "@/components/StatusColumns";
import { PageHeader, ProgressBar, SectionHead, progressLine } from "@/components/ui";
import { useApi } from "@/lib/api";
import { NO_FILTERS, applyFilters, hasFilters, issuesQuery, readFilters } from "@/lib/filters";
import { useFormat, useT } from "@/lib/i18n";
import { STATUS_ORDER, isCanceled, percent } from "@/lib/labels";
import type { BoardIssue, MeetingNoteSummary, Member, Project, StatusCounts } from "@/lib/types";

function countByStatus(issues: BoardIssue[]): StatusCounts {
  const c: StatusCounts = { todo: 0, inProgress: 0, inReview: 0, done: 0 };
  for (const i of issues) {
    if (isCanceled(i)) continue;
    if (i.status === "todo") c.todo++;
    else if (i.status === "in_progress") c.inProgress++;
    else if (i.status === "in_review") c.inReview++;
    else c.done++;
  }
  return c;
}

export default function ProjectsBoardPage() {
  const router = useRouter();
  const t = useT();
  const pt = t.projects;
  const fmt = useFormat();
  const params = useSearchParams();
  const selectedId = params.get("project");
  const filters = readFilters(params);
  const projects = useApi<Project[]>("/api/projects");
  // Progress counts every issue of a project, not only the last 7 days of done work.
  const all = useApi<BoardIssue[]>(issuesQuery(NO_FILTERS, { scope: "all" }));
  const board = useApi<BoardIssue[]>(
    selectedId || hasFilters(filters) ? issuesQuery(filters, selectedId ? { projectId: selectedId } : {}) : null,
  );
  const notes = useApi<MeetingNoteSummary[]>("/api/meeting-notes");
  const members = useApi<Member[]>(filters.person ? "/api/members" : null);

  const error = projects.error ?? all.error ?? notes.error;
  const header = (
    <PageHeader meta={t.board.meta} title={pt.title}>
      {pt.lead}
    </PageHeader>
  );
  if (error)
    return (
      <div className="flex flex-col gap-4">
        {header}
        <ErrorBox error={error} />
      </div>
    );
  if (!projects.data || !all.data || !notes.data)
    return (
      <div className="flex flex-col gap-4">
        {header}
        <Loading />
      </div>
    );

  const selected = projects.data.find((p) => p.id === selectedId) ?? null;
  const select = (id: string | null) => {
    const next = new URLSearchParams(params.toString());
    if (id) next.set("project", id);
    else next.delete("project");
    next.delete("issue");
    const qs = next.toString();
    router.push(qs ? `/projects?${qs}` : "/projects", { scroll: false });
  };
  const sprintName = (id: string) => all.data!.find((i) => i.sprint?.id === id)?.sprint?.name;
  const memberName = (login: string) => {
    const m = members.data?.find((x) => x.githubLogin === login);
    return m ? fmt.person(m) : login;
  };

  const stats = projects.data.map((p) => {
    const counts = countByStatus(all.data!.filter((i) => i.projectIds.includes(p.id)));
    const total = counts.todo + counts.inProgress + counts.inReview + counts.done;
    return { project: p, counts, total, pct: percent(counts.done, total) };
  });
  const leading = [...stats].filter((s) => s.total > 0).sort((a, b) => b.pct - a.pct)[0];
  const showBoard = selected !== null || hasFilters(filters);
  const boardIssues = board.data
    ? applyFilters(board.data, filters).filter((i) => !selected || i.projectIds.includes(selected.id))
    : null;

  return (
    <div className="flex flex-col gap-4">
      <PageHeader meta={t.board.meta} title={pt.title}>
        {stats.length === 0
          ? pt.none
          : leading
            ? pt.leading(stats.length, leading.project.name, leading.pct)
            : pt.noIssues(stats.length)}
      </PageHeader>

      {stats.length > 0 && (
        <div className="reveal mt-2 grid grid-cols-1 gap-3 [animation-delay:30ms] md:grid-cols-2 xl:grid-cols-3">
          {stats.map(({ project: p, counts, total, pct }) => {
            const recentNotes = notes.data!.filter((n) => n.project?.id === p.id).slice(0, 2);
            const isSelected = p.id === selected?.id;
            return (
              <button
                key={p.id}
                type="button"
                aria-pressed={isSelected}
                onClick={() => select(isSelected ? null : p.id)}
                className={`flex cursor-pointer flex-col rounded-card border bg-surface px-3.5 pb-3 pt-2.5 text-left transition-colors duration-[120ms] ease-tool ${
                  isSelected ? "border-ink shadow-[inset_0_0_0_1px_var(--wb-ink)]" : "border-rule hover:border-rule-strong"
                }`}
              >
                <div className="mb-2 flex min-w-0 items-center gap-2">
                  <span aria-hidden className="h-2 w-2 shrink-0 rounded-mark" style={{ background: p.color ?? "var(--wb-muted)" }} />
                  <h2 className="shrink-0 text-heading font-semibold">{p.name}</h2>
                  <span className="truncate font-mono text-[11.5px] text-muted">{p.repositories.map((r) => r.name).join(" · ") || t.common.noRepos}</span>
                  <span className="ml-auto shrink-0 font-mono text-ui font-medium">{pct}%</span>
                </div>
                <ProgressBar counts={counts} />
                <p className="mt-1.5 text-meta text-muted">
                  {total > 0 ? progressLine(counts, t) : ""}
                  {total > 0 && pt.breakdown(counts.inProgress, counts.inReview, counts.todo)}
                </p>
                {recentNotes.length > 0 && (
                  <ul className="mt-2 border-t border-rule pt-1.5 text-meta text-muted">
                    {recentNotes.map((n) => (
                      <li key={n.id} className="truncate">
                        {pt.meeting(n.title, fmt.date(n.heldAt))}
                      </li>
                    ))}
                  </ul>
                )}
              </button>
            );
          })}
        </div>
      )}

      {showBoard && (
        <section className="mt-2 flex flex-col gap-3">
          <SectionHead
            hint={selected ? pt.project : pt.filter}
            title={selected ? selected.name : t.board.matching}
            action={
              selected && (
                <Link href={`/notes?project=${selected.id}`} className="link text-ink-soft">
                  {pt.notesLink}
                </Link>
              )
            }
          />
          <FilterChips filters={filters} sprintName={sprintName} personName={memberName} />
          {board.error ? (
            <ErrorBox error={board.error} />
          ) : !boardIssues ? (
            <Loading />
          ) : boardIssues.length === 0 ? (
            <Empty>{t.board.noMatch}</Empty>
          ) : (
            <StatusColumns issues={boardIssues} statuses={filters.statuses.length ? filters.statuses : STATUS_ORDER} />
          )}
        </section>
      )}
    </div>
  );
}
