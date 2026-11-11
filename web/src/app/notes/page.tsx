"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Empty, ErrorBox, Loading } from "@/components/Feedback";
import { Flag, PageHeader } from "@/components/ui";
import { ROW_ATTR } from "@/components/useShortcuts";
import { useApi } from "@/lib/api";
import { useFormat, useT } from "@/lib/i18n";
import type { MeetingNoteSummary } from "@/lib/types";

export default function MeetingNotesPage() {
  const projectId = useSearchParams().get("project");
  const t = useT();
  const nt = t.notes;
  const fmt = useFormat();
  const notes = useApi<MeetingNoteSummary[]>(
    projectId ? `/api/meeting-notes?projectId=${encodeURIComponent(projectId)}` : "/api/meeting-notes",
  );

  const list = notes.data ?? [];
  const unlinked = list.reduce((sum, n) => sum + (n.actionItemCount - n.linkedActionItemCount), 0);
  const projectName = projectId ? list.find((n) => n.project?.id === projectId)?.project?.name : null;

  return (
    <div className="flex max-w-[900px] flex-col gap-6">
      <PageHeader meta={projectName ? nt.metaProject(projectName) : nt.meta} title={nt.title}>
        {notes.data ? (unlinked > 0 ? nt.unlinked(unlinked) : nt.allLinked) : nt.intro}
      </PageHeader>
      {projectId && (
        <Link href="/notes" className="link -mt-3 self-start text-small text-ink-soft">
          {nt.allProjects}
        </Link>
      )}
      {notes.error ? (
        <ErrorBox error={notes.error} />
      ) : !notes.data ? (
        <Loading />
      ) : list.length === 0 ? (
        <Empty>{nt.empty}</Empty>
      ) : (
        <ul className="card reveal [animation-delay:30ms]">
          {list.map((n) => {
            const missing = n.actionItemCount - n.linkedActionItemCount;
            return (
              <li key={n.id} className="relative after:absolute after:inset-x-5 after:top-0 after:h-px after:bg-rule first:after:hidden">
                <Link
                  href={`/notes/view?id=${n.id}`}
                  {...{ [ROW_ATTR]: "" }}
                  className="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-4 gap-y-1.5 px-5 py-4 transition-colors duration-[120ms] ease-tool hover:bg-hover focus-visible:bg-hover focus-visible:-outline-offset-2 max-[640px]:grid-cols-1"
                >
                  <div className="min-w-0">
                    <p className="truncate text-ui font-medium">{n.title}</p>
                    <p className="text-small text-muted">
                      {fmt.date(n.heldAt)}
                      {n.project && <span> · {n.project.name}</span>}
                    </p>
                  </div>
                  <div className="flex items-center gap-2 text-small text-ink-soft">
                    {n.actionItemCount === 0 ? (
                      <span className="text-muted">{nt.noItems}</span>
                    ) : (
                      <span>
                        {nt.items} <b className="font-mono text-meta font-medium">{n.actionItemCount}</b> {nt.linked}{" "}
                        <b className="font-mono text-meta font-medium">{n.linkedActionItemCount}</b>
                      </span>
                    )}
                    {missing > 0 && <Flag>{nt.missing(missing)}</Flag>}
                  </div>
                </Link>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
