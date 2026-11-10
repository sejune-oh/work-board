"use client";

import { ErrorBox, Loading } from "@/components/Feedback";
import { IssueCard } from "@/components/IssueCard";
import { useApi } from "@/lib/api";
import { STATUS_DOT, STATUS_LABEL, STATUS_ORDER, groupByStatus, personName } from "@/lib/labels";
import type { BoardIssue, Member } from "@/lib/types";

type Row = { key: string; name: string; issues: BoardIssue[] };

export default function PeopleBoardPage() {
  const members = useApi<Member[]>("/api/members");
  const issues = useApi<BoardIssue[]>("/api/issues");

  const error = members.error ?? issues.error;
  if (error) return <ErrorBox error={error} />;
  if (!members.data || !issues.data) return <Loading />;

  const rows: Row[] = members.data.map((m) => ({
    key: m.githubLogin,
    name: personName(m),
    issues: issues.data!.filter((i) => i.assignees.some((a) => a.githubUserId === m.githubUserId)),
  }));
  const unassigned = issues.data.filter((i) => i.assignees.length === 0);
  if (unassigned.length) rows.push({ key: "__none", name: "담당자 없음", issues: unassigned });

  const activeCount = (r: Row) => r.issues.filter((i) => i.category === "in_progress").length;
  const busiest = Math.max(0, ...rows.map(activeCount));

  return (
    <div className="flex flex-col gap-4">
      <div>
        <h1 className="text-xl font-bold">사람별 보드</h1>
        <p className="text-sm text-slate-500">사람마다 지금 무엇을 하는지, 일이 몰린 사람이 누구인지 봅니다.</p>
      </div>

      <div className="overflow-x-auto">
        <table className="w-full min-w-[960px] border-separate border-spacing-0 text-sm">
          <thead>
            <tr>
              <th className="w-40 border-b border-slate-200 p-2 text-left font-semibold dark:border-slate-800">사람</th>
              {STATUS_ORDER.map((s) => (
                <th key={s} className="border-b border-slate-200 p-2 text-left font-semibold dark:border-slate-800">
                  <span className="inline-flex items-center gap-1.5">
                    <span className={`h-2 w-2 rounded-full ${STATUS_DOT[s]}`} />
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
              return (
                <tr key={row.key} className="align-top">
                  <td className="border-b border-slate-100 p-2 dark:border-slate-900">
                    <p className="font-medium">{row.name}</p>
                    <p className="text-xs text-slate-500">진행 중 {active}건 · 전체 {row.issues.length}건</p>
                    {active > 0 && active === busiest && (
                      <span className="mt-1 inline-block rounded bg-amber-100 px-1.5 py-0.5 text-xs text-amber-800 dark:bg-amber-950 dark:text-amber-300">
                        진행 중 최다
                      </span>
                    )}
                  </td>
                  {STATUS_ORDER.map((s) => (
                    <td key={s} className="border-b border-slate-100 p-2 dark:border-slate-900">
                      <div className="flex flex-col gap-2">
                        {groups[s].map((i) => (
                          <IssueCard key={i.id} issue={i} showAssignee={false} />
                        ))}
                      </div>
                    </td>
                  ))}
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
