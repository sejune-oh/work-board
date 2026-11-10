"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Empty, ErrorBox, Loading } from "@/components/Feedback";
import { useIssuePanel } from "@/components/useIssuePanel";
import { Card, Kbd, PageHeader, StatusBadge } from "@/components/ui";
import { ROW_ATTR } from "@/components/useShortcuts";
import { useApi } from "@/lib/api";
import type { SearchResult } from "@/lib/types";

const rowClass =
  "flex w-full cursor-pointer items-center justify-between gap-3 px-3.5 py-2 text-left text-ui transition-colors duration-[120ms] ease-tool hover:bg-hover focus-visible:bg-hover focus-visible:-outline-offset-2";
const row = { [ROW_ATTR]: "" };

export default function SearchPage() {
  const q = useSearchParams().get("q") ?? "";
  const { open } = useIssuePanel();
  const result = useApi<SearchResult>(q.length >= 2 ? `/api/search?q=${encodeURIComponent(q)}` : null);

  if (q.length < 2)
    return (
      <div className="flex max-w-[820px] flex-col gap-6">
        <PageHeader meta="검색" title="검색" />
        <Empty>
          두 글자 이상 입력하세요. 어느 화면에서든 <Kbd>/</Kbd> 키로 검색창에 갈 수 있습니다.
        </Empty>
      </div>
    );
  const r = result.data;
  const total = r ? r.issues.length + r.comments.length + r.meetingNotes.length : 0;

  return (
    <div className="flex max-w-[820px] flex-col gap-4">
      <PageHeader meta="검색" title={<>&ldquo;{q}&rdquo;</>}>
        {r ? (total === 0 ? "맞는 이슈, 대화, 회의 노트가 없습니다." : `${total}건을 찾았습니다.`) : "찾는 중…"}
      </PageHeader>
      {result.error ? (
        <ErrorBox error={result.error} />
      ) : !r ? (
        <Loading />
      ) : (
        <>
          <Group title="이슈" count={r.issues.length}>
            {r.issues.map((i) => (
              <li key={i.id} className="border-b border-rule last:border-b-0">
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

          <Group title="대화" count={r.comments.length}>
            {r.comments.map((c, idx) => (
              <li key={`${c.issueId}-${idx}`} className="border-b border-rule last:border-b-0">
                <button type="button" {...row} onClick={() => open(c.issueId)} className={`${rowClass} flex-col !items-start gap-1`}>
                  <span className="text-meta text-muted">{c.issueTitle}</span>
                  <span className="w-full truncate border-l-2 border-rule-strong pl-2.5 text-ink-soft">{c.snippet}</span>
                </button>
              </li>
            ))}
          </Group>

          <Group title="회의 노트" count={r.meetingNotes.length}>
            {r.meetingNotes.map((n) => (
              <li key={n.id} className="border-b border-rule last:border-b-0">
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
  return (
    <Card title={title} count={count} className="reveal [animation-delay:30ms]">
      {count === 0 ? <p className="px-3.5 py-2.5 text-meta text-muted">없음</p> : <ul>{children}</ul>}
    </Card>
  );
}
