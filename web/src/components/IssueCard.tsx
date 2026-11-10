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
      className={`group block w-full cursor-pointer overflow-hidden rounded-item px-4 py-3.5 text-left transition-shadow duration-[120ms] ease-tool hover:shadow-[0_0_0_1px_var(--wb-rule-strong),0_4px_12px_-6px_rgb(16_19_23/0.16)] ${
        done ? "shadow-[0_0_0_1px_var(--wb-ring)]" : "bg-surface shadow-elev-1"
      } ${urgent && issue.status === "todo" ? "gutter" : ""}`}
    >
      <div className={`flex min-h-[22px] items-center gap-2 text-meta text-muted ${fade}`}>
        <span className="truncate">
          <span className="font-mono">
            {issue.repository.name} #{issue.number}
          </span>
          {issue.issueType && ` · ${fmt.issueType(issue.issueType)}`}
        </span>
        <span className="ml-auto shrink-0">
          {urgent && <Flag fill>{t.priority.urgent}</Flag>}
          {high && <span className="text-meta text-ink-soft">{t.priority.high}</span>}
        </span>
      </div>
      <p
        className={`mt-1.5 line-clamp-2 text-ui font-medium ${done ? "text-muted" : "text-ink"} ${canceled ? "line-through" : ""} ${fade}`}
      >
        {issue.title}
      </p>
      {showStatus && !canceled && (
        <div className={`mt-1.5 ${fade}`}>
          <StatusBadge status={issue.status} />
        </div>
      )}
      <div className="mt-2 flex items-center justify-between gap-2 text-meta text-muted">
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
            <span title={t.common.staleHint(STALE_DAYS)}>{fmt.relativeDays(issue.updatedAt)}</span>
          )}
        </span>
      </div>
    </button>
  );
}
