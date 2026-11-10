import { CANCELED_DOT, CANCELED_LABEL, STATUS_DOT, STATUS_LABEL, STATUS_ORDER, groupByStatus, isCanceled } from "@/lib/labels";
import type { BoardIssue, Status } from "@/lib/types";
import { IssueCard } from "./IssueCard";
import { Dot } from "./ui";

type Props = { issues: BoardIssue[]; showAssignee?: boolean; statuses?: Status[] };

export function StatusColumns({ issues, showAssignee = true, statuses = STATUS_ORDER }: Props) {
  const groups = groupByStatus(issues);
  const canceled = issues.filter(isCanceled);
  const cols = statuses.length >= 4 ? "md:grid-cols-2 xl:grid-cols-4" : statuses.length === 3 ? "md:grid-cols-3" : statuses.length === 2 ? "md:grid-cols-2" : "";

  return (
    <div className={`grid grid-cols-1 gap-3 ${cols}`}>
      {statuses.map((status) => (
        <section key={status} className="min-w-0 rounded-card bg-sunken p-3">
          <h3 className="mb-2.5 flex items-center gap-2 text-ui font-semibold text-ink">
            <Dot className={STATUS_DOT[status]} />
            {STATUS_LABEL[status]}
            <span className="font-mono text-meta font-normal text-muted">{groups[status].length}</span>
          </h3>
          <div className="flex flex-col gap-2">
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
      <summary className="flex cursor-pointer list-none items-center gap-2 rounded-card px-1 py-1 text-meta text-muted hover:text-ink-soft">
        <span aria-hidden className="transition-transform duration-200 ease-paper group-open:rotate-90">›</span>
        <Dot className={CANCELED_DOT} />
        {CANCELED_LABEL}
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
