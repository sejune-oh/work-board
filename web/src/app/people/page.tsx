"use client";

import { useSearchParams } from "next/navigation";
import { ErrorBox, Loading } from "@/components/Feedback";
import { FilterChips } from "@/components/FilterChips";
import { IssueCard } from "@/components/IssueCard";
import { CanceledGroup } from "@/components/StatusColumns";
import { Avatar, Dot, Flag, PageHeader } from "@/components/ui";
import { useApi } from "@/lib/api";
import { applyFilters, hasFilters, issuesQuery, readFilters } from "@/lib/filters";
import { useFormat, useT } from "@/lib/i18n";
import { STATUS_DOT, STATUS_ORDER, groupByStatus, isCanceled, isCrowded } from "@/lib/labels";
import { useMe } from "@/lib/me";
import type { BoardIssue, Member } from "@/lib/types";

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
        <p className="rounded-control border border-dashed border-rule-strong px-3 py-2.5 text-meta text-muted">
          {members.data.length === 0 ? pt.noMembers : pt.noMatch}
        </p>
      ) : (
        <div className="card reveal mt-2 overflow-x-auto [animation-delay:30ms]">
          <table className="w-full min-w-[960px] border-separate border-spacing-0 text-ui">
            <thead>
              <tr>
                <th className="sticky left-0 z-[1] h-[30px] w-48 border-b border-rule bg-canvas px-3 text-left font-mono text-[11px] font-normal text-muted">{pt.person}</th>
                {statuses.map((s) => (
                  <th key={s} className="h-[30px] border-b border-l border-rule bg-canvas px-3 text-left text-meta font-semibold text-ink-soft">
                    <span className="inline-flex items-center gap-1.5">
                      <Dot className={STATUS_DOT[s]} />
                      {t.status[s]}
                    </span>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => {
                const groups = groupByStatus(row.issues);
                const active = activeCount(row);
                const isCrowdedRow = crowded.includes(row);
                return (
                  <tr key={row.key} className="align-top [&:last-child>td]:border-b-0">
                    <td className="sticky left-0 z-[1] border-b border-rule bg-surface px-3 py-2.5">
                      {row.member ? (
                        <div className="flex items-start gap-2.5">
                          <Avatar person={row.member} size="lg" />
                          <div className="min-w-0">
                            <p className="flex items-center gap-1.5 font-semibold">
                              {fmt.person(row.member)}
                              {row.member.githubUserId === myId && (
                                <span className="rounded-[3px] border border-rule px-1 font-mono text-[10px] font-medium text-muted">{t.common.me}</span>
                              )}
                            </p>
                            <p className="font-mono text-meta text-muted">
                              {pt.activeLabel} <b className="font-medium text-ink">{active}</b>
                              {pt.total(row.issues.length)}
                            </p>
                            {isCrowdedRow && (
                              <Flag className="mt-1.5">{t.common.crowded}</Flag>
                            )}
                          </div>
                        </div>
                      ) : (
                        <div className="flex items-start gap-2.5">
                          <span aria-hidden className="h-7 w-7 shrink-0 rounded-full border border-dashed border-accent" />
                          <div>
                            <p className="font-semibold text-accent">{t.common.unassigned}</p>
                            <p className="font-mono text-meta text-muted">{pt.openCount(row.issues.length)}</p>
                          </div>
                        </div>
                      )}
                    </td>
                    {statuses.map((s) => (
                      <td key={s} className="border-b border-l border-rule bg-canvas p-2">
                        <div className="flex flex-col gap-1.5">
                          {groups[s].map((i) => (
                            <IssueCard key={i.id} issue={i} showAssignee={false} />
                          ))}
                          {s === "done" && <CanceledGroup issues={row.issues.filter(isCanceled)} showAssignee={false} />}
                        </div>
                      </td>
                    ))}
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
