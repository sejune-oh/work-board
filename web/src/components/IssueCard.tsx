"use client";

import { PRIORITY_LABEL, PRIORITY_STYLE, STALE_DAYS, TYPE_LABEL, daysSince, personName, relativeDays } from "@/lib/labels";
import type { BoardIssue } from "@/lib/types";
import { useIssuePanel } from "./useIssuePanel";

export function IssueCard({ issue, showAssignee = true }: { issue: BoardIssue; showAssignee?: boolean }) {
  const { open } = useIssuePanel();
  const stale = issue.state === "open" && daysSince(issue.updatedAt) >= STALE_DAYS;

  return (
    <button
      type="button"
      onClick={() => open(issue.id)}
      className={`w-full rounded-lg border border-slate-200 bg-white p-3 text-left shadow-sm transition hover:border-blue-400 hover:shadow dark:border-slate-700 dark:bg-slate-900 ${stale ? "opacity-55" : ""}`}
    >
      <div className="flex items-center gap-2 text-xs text-slate-500">
        <span className="truncate">
          {issue.repository.name} #{issue.number}
        </span>
        {issue.issueType && (
          <span className="shrink-0 rounded bg-slate-100 px-1.5 py-0.5 dark:bg-slate-800">
            {TYPE_LABEL[issue.issueType] ?? issue.issueType}
          </span>
        )}
        {issue.priority && (issue.priority === "urgent" || issue.priority === "high") && (
          <span className={`shrink-0 rounded px-1.5 py-0.5 ${PRIORITY_STYLE[issue.priority]}`}>
            {PRIORITY_LABEL[issue.priority]}
          </span>
        )}
      </div>
      <p className="mt-1 line-clamp-2 text-sm font-medium text-slate-900 dark:text-slate-100">{issue.title}</p>
      <div className="mt-2 flex items-center justify-between gap-2 text-xs text-slate-500">
        <span className="truncate">
          {showAssignee && (issue.assignees.length ? issue.assignees.map(personName).join(", ") : "담당자 없음")}
        </span>
        <span className="flex shrink-0 items-center gap-2">
          {issue.openPullRequestCount > 0 && <span title="열린 PR">PR {issue.openPullRequestCount}</span>}
          {issue.commentCount > 0 && <span>댓글 {issue.commentCount}</span>}
          <span title={stale ? `${STALE_DAYS}일 이상 움직임 없음` : "마지막 활동"}>{relativeDays(issue.updatedAt)}</span>
        </span>
      </div>
    </button>
  );
}
