import { STATUS_DOT, STATUS_LABEL, STATUS_ORDER, groupByStatus } from "@/lib/labels";
import type { BoardIssue } from "@/lib/types";
import { IssueCard } from "./IssueCard";

export function StatusColumns({ issues, showAssignee = true }: { issues: BoardIssue[]; showAssignee?: boolean }) {
  const groups = groupByStatus(issues);
  return (
    <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-4">
      {STATUS_ORDER.map((status) => (
        <section key={status} className="rounded-xl bg-slate-50 p-3 dark:bg-slate-900/50">
          <h3 className="mb-2 flex items-center gap-2 text-sm font-semibold text-slate-700 dark:text-slate-200">
            <span className={`h-2 w-2 rounded-full ${STATUS_DOT[status]}`} />
            {STATUS_LABEL[status]}
            <span className="font-normal text-slate-400">{groups[status].length}</span>
          </h3>
          <div className="flex flex-col gap-2">
            {groups[status].map((issue) => (
              <IssueCard key={issue.id} issue={issue} showAssignee={showAssignee} />
            ))}
          </div>
        </section>
      ))}
    </div>
  );
}
