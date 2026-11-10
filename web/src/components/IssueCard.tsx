"use client";

import { useFormat, useT } from "@/lib/i18n";
import { STALE_DAYS, daysSince, isCanceled, isStale } from "@/lib/labels";
import { useSettings } from "@/lib/me";
import type { BoardIssue } from "@/lib/types";
import { Flag, StatusBadge } from "./ui";
import { ROW_ATTR } from "./useShortcuts";
import { useIssuePanel } from "./useIssuePanel";

type Props = {
  issue: BoardIssue;
  showAssignee?: boolean;
  /** Board columns already name the status, so the status line is off unless asked for. */
  showStatus?: boolean;
};

export function IssueCard({ issue, showAssignee = true, showStatus = false }: Props) {
  const { open } = useIssuePanel();
  const settings = useSettings();
  const t = useT();
  const fmt = useFormat();
  const stale = isStale(issue);
  const canceled = isCanceled(issue);
  const done = issue.status === "done" || canceled;
  const urgent = settings.priorityEnabled && issue.priority === "urgent" && !done;
  const high = settings.priorityEnabled && issue.priority === "high" && !done;
  const unassigned = issue.state === "open" && issue.assignees.length === 0;
  const fade = stale ? "opacity-55" : "";

  return (
    <button
      type="button"
      {...{ [ROW_ATTR]: "" }}
      onClick={() => open(issue.id)}
      className={`group block w-full cursor-pointer overflow-hidden rounded-control border border-rule bg-surface px-3 py-2 text-left transition-colors duration-[120ms] ease-tool hover:border-rule-strong hover:bg-[linear-gradient(var(--wb-hover),var(--wb-hover))] ${
        urgent && issue.status === "todo" ? "gutter" : ""
      }`}
    >
      <div className={`flex items-center gap-2 font-mono text-meta text-muted ${fade}`}>
        <span className="truncate">
          {issue.repository.name} #{issue.number}
          {issue.issueType && ` · ${fmt.issueType(issue.issueType)}`}
        </span>
        <span className="ml-auto shrink-0 font-sans">
          {urgent && <Flag fill>{t.priority.urgent}</Flag>}
          {high && <span className="text-meta text-ink-soft">{t.priority.high}</span>}
        </span>
      </div>
      <p
        className={`mt-0.5 line-clamp-2 text-ui font-medium ${done ? "text-muted" : "text-ink"} ${canceled ? "line-through" : ""} ${fade}`}
      >
        {issue.title}
      </p>
      {showStatus && !canceled && (
        <div className={`mt-1 ${fade}`}>
          <StatusBadge status={issue.status} />
        </div>
      )}
      <div className="mt-1.5 flex items-center justify-between gap-2 text-meta text-muted">
        <span className={`truncate ${unassigned ? "" : fade}`}>
          {showAssignee &&
            (unassigned ? (
              <span className="inline-flex items-center gap-1.5 text-accent">
                <span aria-hidden className="inline-block h-3 w-3 rounded-full border border-dashed border-accent" />
                {t.common.unassigned}
              </span>
            ) : (
              issue.assignees.map(fmt.person).join(", ")
            ))}
        </span>
        <span className="flex shrink-0 items-center gap-2">
          {issue.openPullRequestCount > 0 && (
            <span className={fade} title={t.common.openPrTitle}>
              PR {issue.openPullRequestCount}
            </span>
          )}
          {issue.commentCount > 0 && <span className={fade}>{t.common.comments(issue.commentCount)}</span>}
          {stale ? (
            <Flag>{t.common.stalledFor(daysSince(issue.updatedAt))}</Flag>
          ) : (
            <span className="font-mono" title={t.common.staleHint(STALE_DAYS)}>{fmt.relativeDays(issue.updatedAt)}</span>
          )}
        </span>
      </div>
    </button>
  );
}
