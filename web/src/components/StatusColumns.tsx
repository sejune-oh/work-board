"use client";

import { useT } from "@/lib/i18n";
import { CANCELED_DOT, STATUS_DOT, STATUS_ORDER, groupByStatus, isCanceled } from "@/lib/labels";
import type { BoardIssue, Status } from "@/lib/types";
import { IssueCard } from "./IssueCard";
import { Count, Dot } from "./ui";

type Props = { issues: BoardIssue[]; showAssignee?: boolean; statuses?: Status[] };

export function StatusColumns({ issues, showAssignee = true, statuses = STATUS_ORDER }: Props) {
  const t = useT();
  const groups = groupByStatus(issues);
  const canceled = issues.filter(isCanceled);
  const cols = statuses.length >= 4 ? "md:grid-cols-2 xl:grid-cols-4" : statuses.length === 3 ? "md:grid-cols-3" : statuses.length === 2 ? "md:grid-cols-2" : "";

  return (
    <div className={`grid grid-cols-1 items-start gap-x-4 gap-y-6 ${cols}`}>
      {statuses.map((status) => (
        <section key={status} className="flex min-w-0 flex-col gap-2">
          <h3 className="flex items-center gap-2 px-1 pb-1 text-ui font-semibold text-ink">
            <Dot className={STATUS_DOT[status]} />
            {t.status[status]}
            <Count n={groups[status].length} />
          </h3>
          <div className="flex flex-col gap-2">
            {groups[status].length === 0 && (
              <p className="px-1 py-2 text-small text-muted">{t.common.none}</p>
            )}
            {groups[status].map((issue) => (
              <IssueCard key={issue.id} issue={issue} showAssignee={showAssignee} />
            ))}
            {status === "done" && <CanceledGroup issues={canceled} showAssignee={showAssignee} />}
          </div>
        </section>
      ))}
    </div>
  );
}

/** Canceled work folds under done instead of taking a fifth column; hidden when there is none. */
export function CanceledGroup({ issues, showAssignee = true }: { issues: BoardIssue[]; showAssignee?: boolean }) {
  const t = useT();
  if (issues.length === 0) return null;
  return (
    <details className="group mt-1">
      <summary className="flex cursor-pointer list-none items-center gap-2 rounded-control px-1 py-1.5 text-small text-muted hover:text-ink-soft">
        <span aria-hidden className="transition-transform duration-200 ease-tool group-open:rotate-90">›</span>
        <Dot className={CANCELED_DOT} />
        {t.canceled}
        <span className="font-mono">{issues.length}</span>
      </summary>
      <div className="mt-2 flex flex-col gap-2">
        {issues.map((issue) => (
          <IssueCard key={issue.id} issue={issue} showAssignee={showAssignee} />
        ))}
      </div>
    </details>
  );
}
