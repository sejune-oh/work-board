import { CANCELED_DOT, CANCELED_LABEL, STATUS_DOT, STATUS_LABEL, STATUS_ORDER, groupByStatus, isCanceled } from "@/lib/labels";
import type { BoardIssue, Status } from "@/lib/types";
import { IssueCard } from "./IssueCard";
import { Count, Dot } from "./ui";

type Props = { issues: BoardIssue[]; showAssignee?: boolean; statuses?: Status[] };

export function StatusColumns({ issues, showAssignee = true, statuses = STATUS_ORDER }: Props) {
  const groups = groupByStatus(issues);
  const canceled = issues.filter(isCanceled);
  const cols = statuses.length >= 4 ? "md:grid-cols-2 xl:grid-cols-4" : statuses.length === 3 ? "md:grid-cols-3" : statuses.length === 2 ? "md:grid-cols-2" : "";

  return (
    <div className={`grid grid-cols-1 gap-3 ${cols}`}>
      {statuses.map((status) => (
        <section key={status} className="card min-w-0 bg-canvas">
          <h3 className="flex h-10 items-center gap-2 border-b border-rule bg-surface px-3 text-heading font-semibold text-ink">
            <Dot className={STATUS_DOT[status]} />
            {STATUS_LABEL[status]}
            <Count n={groups[status].length} />
          </h3>
          <div className="flex flex-col gap-1.5 p-2">
            {groups[status].length === 0 && <p className="px-1 text-meta text-muted">없음</p>}
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

/** Canceled work folds under 완료 instead of taking a fifth column; hidden when there is none. */
export function CanceledGroup({ issues, showAssignee = true }: { issues: BoardIssue[]; showAssignee?: boolean }) {
  if (issues.length === 0) return null;
  return (
    <details className="group mt-1">
      <summary className="flex cursor-pointer list-none items-center gap-2 rounded-control px-1 py-1 text-meta text-muted hover:text-ink-soft">
        <span aria-hidden className="transition-transform duration-200 ease-tool group-open:rotate-90">›</span>
        <Dot className={CANCELED_DOT} />
        {CANCELED_LABEL}
        <span className="font-mono">{issues.length}</span>
      </summary>
      <div className="mt-1.5 flex flex-col gap-1.5">
        {issues.map((issue) => (
          <IssueCard key={issue.id} issue={issue} showAssignee={showAssignee} />
        ))}
      </div>
    </details>
  );
}
