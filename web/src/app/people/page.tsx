"use client";

import { useSearchParams } from "next/navigation";
import { ErrorBox, Loading } from "@/components/Feedback";
import { FilterChips } from "@/components/FilterChips";
import { IssueCard } from "@/components/IssueCard";
import { CanceledGroup, boardCols } from "@/components/StatusColumns";
import { Avatar, Dot, Flag, PageHeader } from "@/components/ui";
import { useApi } from "@/lib/api";
import { applyFilters, hasFilters, issuesQuery, readFilters } from "@/lib/filters";
import { useFormat, useT } from "@/lib/i18n";
import { STATUS_DOT, STATUS_ORDER, groupByStatus, isCanceled, isCrowded } from "@/lib/labels";
import { useMe } from "@/lib/me";
import type { BoardIssue, Member, Status } from "@/lib/types";

type Row = { key: string; member: Member | null; issues: BoardIssue[] };

export default function PeopleBoardPage() {
  const filters = readFilters(useSearchParams());
  const me = useMe();
  const t = useT();
  const pt = t.people;
  const fmt = useFormat();
  const members = useApi<Member[]>("/api/members");
  const issues = useApi<BoardIssue[]>(issuesQuery(filters));

  const error = members.error ?? issues.error;
  const filtered = issues.data ? applyFilters(issues.data, filters) : null;
  const memberName = (login: string) => {
    const m = members.data?.find((x) => x.githubLogin === login);
    return m ? fmt.person(m) : login;
  };
  const sprintName = (id: string) => issues.data?.find((i) => i.sprint?.id === id)?.sprint?.name;

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
  if (!members.data || !filtered)
    return (
      <div className="flex flex-col gap-4">
        {header}
        <Loading />
      </div>
    );

  const visibleMembers = filters.person ? members.data.filter((m) => m.githubLogin === filters.person) : members.data;
  const rows: Row[] = visibleMembers.map((m) => ({
    key: m.githubLogin,
    member: m,
    issues: filtered.filter((i) => i.assignees.some((a) => a.githubUserId === m.githubUserId)),
  }));
  const unassigned = filtered.filter((i) => i.assignees.length === 0 && i.state === "open");
  if (!filters.person && unassigned.length) rows.push({ key: "__none", member: null, issues: unassigned });

  const activeCount = (r: Row) => r.issues.filter((i) => i.category === "in_progress").length;
  // The crowding rule compares against the whole team, so it only applies to the unfiltered board.
  const teamActive = rows.filter((r) => r.member).map(activeCount);
  const crowdingOn = !hasFilters(filters);
  const crowded = crowdingOn ? rows.filter((r) => r.member && isCrowded(activeCount(r), teamActive)) : [];
  const statuses = filters.statuses.length ? filters.statuses : STATUS_ORDER;
  const myId = me.data?.member.githubUserId;

  let fact: string;
  if (hasFilters(filters)) fact = pt.filtered(filtered.length);
  else if (crowded.length) fact = pt.crowded(crowded.map((r) => fmt.person(r.member!)).join(", "));
  else fact = pt.balanced(teamActive.filter((n) => n > 0).length);

  return (
    <div className="flex flex-col gap-4">
      <PageHeader meta={t.board.meta} title={pt.title}>
        {fact}
      </PageHeader>
      <FilterChips filters={filters} personName={memberName} sprintName={sprintName} />

      {rows.length === 0 ? (
        <p className="rounded-item border border-dashed border-rule-strong px-4 py-3 text-small text-muted">
          {members.data.length === 0 ? pt.noMembers : pt.noMatch}
        </p>
      ) : (
        <div className="reveal mt-2 flex flex-col gap-10 [animation-delay:30ms]">
          {rows.map((row) => {
            const groups = groupByStatus(row.issues);
            const canceled = row.issues.filter(isCanceled);
            // Canceled work folds under done, so done counts as filled when only canceled issues are there.
            const filled = (s: Status) => groups[s].length > 0 || (s === "done" && canceled.length > 0);
            const active = activeCount(row);
            const isCrowdedRow = crowded.includes(row);
            return (
              <section key={row.key} aria-label={row.member ? fmt.person(row.member) : t.common.unassigned} className="@container flex flex-col gap-4">
                {row.member ? (
                  <header className="flex flex-wrap items-center gap-x-3 gap-y-1">
                    <Avatar person={row.member} size="lg" />
                    <h2 className="flex items-center gap-1.5 text-heading font-semibold">
                      {fmt.person(row.member)}
                      {row.member.githubUserId === myId && <span className="text-small font-normal text-muted">{t.common.meParen}</span>}
                    </h2>
                    <p className="text-small text-muted">
                      {pt.activeLabel} <b className="font-mono text-meta font-medium text-ink">{active}</b>
                      {pt.total(row.issues.length)}
                    </p>
                    {isCrowdedRow && <Flag>{t.common.crowded}</Flag>}
                  </header>
                ) : (
                  <header className="flex flex-wrap items-center gap-x-3 gap-y-1">
                    <span aria-hidden className="h-8 w-8 shrink-0 rounded-full border border-dashed border-accent" />
                    <h2 className="text-heading font-semibold text-accent">{t.common.unassigned}</h2>
                    <p className="text-small text-muted">{pt.openCount(row.issues.length)}</p>
                  </header>
                )}
                <div className={`grid grid-cols-1 items-start gap-x-4 gap-y-5 ${boardCols(statuses.length)}`}>
                  {statuses.map((s) => (
                    <div key={s} className={`flex min-w-0 flex-col gap-2 ${filled(s) ? "" : "@max-[34rem]:hidden"}`}>
                      <h3 className="flex items-center gap-2 px-1 text-small font-semibold text-ink-soft">
                        <Dot className={STATUS_DOT[s]} />
                        {t.status[s]}
                        <span className="font-mono text-meta font-normal text-muted">{groups[s].length}</span>
                      </h3>
                      {!filled(s) && <p className="px-1 py-2 text-small text-muted">{t.common.none}</p>}
                      {groups[s].map((i) => (
                        <IssueCard key={i.id} issue={i} showAssignee={false} />
                      ))}
                      {s === "done" && <CanceledGroup issues={canceled} showAssignee={false} />}
                    </div>
                  ))}
                </div>
              </section>
            );
          })}
        </div>
      )}
    </div>
  );
}
