"use client";

import { useSearchParams } from "next/navigation";
import { ErrorBox, Loading } from "@/components/Feedback";
import { FilterChips } from "@/components/FilterChips";
import { IssueCard } from "@/components/IssueCard";
import { CanceledGroup } from "@/components/StatusColumns";
import { Avatar, Dot, PageHeader, Stamp } from "@/components/ui";
import { useApi } from "@/lib/api";
import { applyFilters, hasFilters, issuesQuery, readFilters } from "@/lib/filters";
import { STATUS_DOT, STATUS_LABEL, STATUS_ORDER, groupByStatus, isCanceled, isCrowded, personName } from "@/lib/labels";
import { useMe } from "@/lib/me";
import type { BoardIssue, Member } from "@/lib/types";

type Row = { key: string; member: Member | null; issues: BoardIssue[] };

export default function PeopleBoardPage() {
  const filters = readFilters(useSearchParams());
  const me = useMe();
  const members = useApi<Member[]>("/api/members");
  const issues = useApi<BoardIssue[]>(issuesQuery(filters));

  const error = members.error ?? issues.error;
  const filtered = issues.data ? applyFilters(issues.data, filters) : null;
  const memberName = (login: string) => {
    const m = members.data?.find((x) => x.githubLogin === login);
    return m ? personName(m) : login;
  };
  const sprintName = (id: string) => issues.data?.find((i) => i.sprint?.id === id)?.sprint?.name ?? "주기";

  const header = (
    <PageHeader eyebrow="보드" title="사람별 보드">
      사람마다 지금 무엇을 하는지, 일이 몰린 사람이 있는지 봅니다.
    </PageHeader>
  );

  if (error)
    return (
      <div className="flex flex-col gap-6">
        {header}
        <ErrorBox error={error} />
      </div>
    );
  if (!members.data || !filtered)
    return (
      <div className="flex flex-col gap-6">
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
  if (hasFilters(filters)) fact = `조건에 맞는 일 ${filtered.length}건`;
  else if (crowded.length)
    fact = `${crowded.map((r) => personName(r.member!)).join(", ")}에게 진행 중인 일이 몰려 있습니다.`;
  else fact = `몰린 사람 없이 ${teamActive.filter((n) => n > 0).length}명이 진행 중인 일을 나눠 맡고 있습니다.`;

  return (
    <div className="flex flex-col gap-6">
      <PageHeader eyebrow="보드" title="사람별 보드">
        {fact}
      </PageHeader>
      <FilterChips filters={filters} personName={memberName} sprintName={sprintName} />

      {rows.length === 0 ? (
        <p className="rounded-card border border-dashed border-rule px-3 py-2.5 text-meta text-muted">
          {members.data.length === 0 ? "팀원이 없습니다. 설정에서 GitHub 조직을 연결하세요." : "이 조건에 맞는 사람이 없습니다."}
        </p>
      ) : (
        <div className="reveal overflow-x-auto [animation-delay:40ms]">
          <table className="w-full min-w-[960px] border-separate border-spacing-0 text-ui">
            <thead>
              <tr>
                <th className="w-44 border-b-2 border-rule-strong px-2 pb-2 text-left font-semibold">사람</th>
                {statuses.map((s) => (
                  <th key={s} className="border-b-2 border-rule-strong px-2 pb-2 text-left font-semibold">
                    <span className="inline-flex items-center gap-1.5">
                      <Dot className={STATUS_DOT[s]} />
                      {STATUS_LABEL[s]}
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
                  <tr key={row.key} className="align-top">
                    <td className="border-b border-rule px-2 py-3">
                      {row.member ? (
                        <div className="flex items-start gap-2.5">
                          <Avatar person={row.member} size="lg" />
                          <div className="min-w-0">
                            <p className="flex items-center gap-1.5 font-semibold">
                              {personName(row.member)}
                              {row.member.githubUserId === myId && (
                                <span className="rounded-mark border border-rule px-1 text-[11px] font-medium text-muted">나</span>
                              )}
                            </p>
                            <p className="text-meta text-muted">
                              진행·리뷰 {active} · 전체 {row.issues.length}
                            </p>
                            {isCrowdedRow && (
                              <Stamp className="mt-1.5" >몰림</Stamp>
                            )}
                          </div>
                        </div>
                      ) : (
                        <div className="flex items-start gap-2.5">
                          <span aria-hidden className="h-[34px] w-[34px] shrink-0 rounded-full border border-dashed border-accent" />
                          <div>
                            <p className="font-semibold text-accent">담당자 없음</p>
                            <p className="text-meta text-muted">열린 일 {row.issues.length}</p>
                          </div>
                        </div>
                      )}
                    </td>
                    {statuses.map((s) => (
                      <td key={s} className="border-b border-rule px-2 py-3">
                        <div className="flex flex-col gap-2">
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
