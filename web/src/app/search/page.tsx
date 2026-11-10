"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Empty, ErrorBox, Loading } from "@/components/Feedback";
import { StatusBadge } from "@/components/StatusBadge";
import { useIssuePanel } from "@/components/useIssuePanel";
import { useApi } from "@/lib/api";
import type { SearchResult } from "@/lib/types";

export default function SearchPage() {
  const q = useSearchParams().get("q") ?? "";
  const { open } = useIssuePanel();
  const result = useApi<SearchResult>(q.length >= 2 ? `/api/search?q=${encodeURIComponent(q)}` : null);

  if (q.length < 2) return <Empty>두 글자 이상 입력하세요.</Empty>;
  if (result.error) return <ErrorBox error={result.error} />;
  if (!result.data) return <Loading />;
  const r = result.data;
  const total = r.issues.length + r.comments.length + r.meetingNotes.length;

  return (
    <div className="flex max-w-3xl flex-col gap-6">
      <h1 className="text-xl font-bold">
        &ldquo;{q}&rdquo; 검색 결과 <span className="text-base font-normal text-slate-500">{total}건</span>
      </h1>

      <Group title="이슈" count={r.issues.length}>
        {r.issues.map((i) => (
          <li key={i.id}>
            <button type="button" onClick={() => open(i.id)} className="flex w-full items-center justify-between gap-3 text-left text-sm hover:text-blue-600">
              <span className="truncate">{i.repositoryName} #{i.number} {i.title}</span>
              <StatusBadge status={i.status} />
            </button>
          </li>
        ))}
      </Group>

      <Group title="대화" count={r.comments.length}>
        {r.comments.map((c, idx) => (
          <li key={`${c.issueId}-${idx}`}>
            <button type="button" onClick={() => open(c.issueId)} className="w-full text-left text-sm hover:text-blue-600">
              <span className="text-xs text-slate-500">{c.issueTitle}</span>
              <p className="truncate">{c.snippet}</p>
            </button>
          </li>
        ))}
      </Group>

      <Group title="회의 노트" count={r.meetingNotes.length}>
        {r.meetingNotes.map((n) => (
          <li key={n.id}>
            <Link href={`/notes/view?id=${n.id}`} className="text-sm hover:text-blue-600">{n.title}</Link>
          </li>
        ))}
      </Group>
    </div>
  );
}

function Group({ title, count, children }: { title: string; count: number; children: React.ReactNode }) {
  return (
    <section>
      <h2 className="mb-2 text-sm font-semibold">
        {title} <span className="font-normal text-slate-400">{count}</span>
      </h2>
      {count === 0 ? <p className="text-sm text-slate-400">없음</p> : <ul className="flex flex-col gap-2">{children}</ul>}
    </section>
  );
}
