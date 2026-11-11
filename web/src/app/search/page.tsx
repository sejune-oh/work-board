"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Empty, ErrorBox, Loading } from "@/components/Feedback";
import { useIssuePanel } from "@/components/useIssuePanel";
import { Card, Kbd, PageHeader, StatusBadge } from "@/components/ui";
import { ROW_ATTR } from "@/components/useShortcuts";
import { useApi } from "@/lib/api";
import { useT } from "@/lib/i18n";
import type { SearchResult } from "@/lib/types";

const rowClass =
  "flex w-full cursor-pointer items-center justify-between gap-3 px-5 py-3 text-left text-ui transition-colors duration-[120ms] ease-tool hover:bg-hover focus-visible:bg-hover focus-visible:-outline-offset-2";
const row = { [ROW_ATTR]: "" };

export default function SearchPage() {
  const q = useSearchParams().get("q") ?? "";
  const { open } = useIssuePanel();
  const t = useT();
  const sr = t.search;
  const result = useApi<SearchResult>(q.length >= 2 ? `/api/search?q=${encodeURIComponent(q)}` : null);

  if (q.length < 2)
    return (
      <div className="flex max-w-[820px] flex-col gap-6">
        <PageHeader meta={sr.title} title={sr.title} />
        <Empty>
          {sr.tooShortBefore}
          <Kbd>/</Kbd>
          {sr.tooShortAfter}
        </Empty>
      </div>
    );
  const r = result.data;
  const total = r ? r.issues.length + r.comments.length + r.meetingNotes.length : 0;

  return (
    <div className="flex max-w-[820px] flex-col gap-6">
      <PageHeader meta={sr.title} title={<>&ldquo;{q}&rdquo;</>}>
        {r ? (total === 0 ? sr.none : sr.found(total)) : sr.searching}
      </PageHeader>
      {result.error ? (
        <ErrorBox error={result.error} />
      ) : !r ? (
        <Loading />
      ) : (
        <>
          <Group title={sr.issues} count={r.issues.length}>
            {r.issues.map((i) => (
              <li key={i.id} className="relative after:absolute after:inset-x-5 after:top-0 after:h-px after:bg-rule first:after:hidden">
                <button type="button" {...row} onClick={() => open(i.id)} className={rowClass}>
                  <span className="min-w-0 truncate">
                    <span className="font-mono text-meta text-muted">
                      {i.repositoryName} #{i.number}
                    </span>{" "}
                    <span className="font-medium">{i.title}</span>
                  </span>
                  <StatusBadge status={i.status} />
                </button>
              </li>
            ))}
          </Group>

          <Group title={sr.comments} count={r.comments.length}>
            {r.comments.map((c, idx) => (
              <li key={`${c.issueId}-${idx}`} className="relative after:absolute after:inset-x-5 after:top-0 after:h-px after:bg-rule first:after:hidden">
                <button type="button" {...row} onClick={() => open(c.issueId)} className={`${rowClass} flex-col !items-start gap-1`}>
                  <span className="text-small text-muted">{c.issueTitle}</span>
                  <span className="w-full truncate border-l-2 border-rule-strong pl-2.5 text-ink-soft">{c.snippet}</span>
                </button>
              </li>
            ))}
          </Group>

          <Group title={sr.notes} count={r.meetingNotes.length}>
            {r.meetingNotes.map((n) => (
              <li key={n.id} className="relative after:absolute after:inset-x-5 after:top-0 after:h-px after:bg-rule first:after:hidden">
                <Link href={`/notes/view?id=${n.id}`} {...row} className={rowClass}>
                  <span className="truncate font-medium">{n.title}</span>
                </Link>
              </li>
            ))}
          </Group>
        </>
      )}
    </div>
  );
}

function Group({ title, count, children }: { title: string; count: number; children: React.ReactNode }) {
  const t = useT();
  return (
    <Card title={title} count={count} className="reveal [animation-delay:30ms]">
      {count === 0 ? <p className="px-5 py-4 text-small text-muted">{t.common.none}</p> : <ul>{children}</ul>}
    </Card>
  );
}
