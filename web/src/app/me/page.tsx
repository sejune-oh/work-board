"use client";

import { ErrorBox, Loading } from "@/components/Feedback";
import { StatusColumns } from "@/components/StatusColumns";
import { PageHeader } from "@/components/ui";
import { useApi } from "@/lib/api";
import { isStale, personName } from "@/lib/labels";
import { useMe } from "@/lib/me";
import type { BoardIssue } from "@/lib/types";

export default function MyWorkPage() {
  const me = useMe();
  const login = me.data?.member.githubLogin;
  const issues = useApi<BoardIssue[]>(login ? `/api/issues?assignee=${encodeURIComponent(login)}` : null);

  if (me.error) return <ErrorBox error={me.error} />;
  if (!me.data) return <Loading />;

  const list = issues.data ?? [];
  const review = list.filter((i) => i.status === "in_review").length;
  const progress = list.filter((i) => i.status === "in_progress").length;
  const stale = list.filter(isStale).length;
  const fact = !issues.data
    ? `${personName(me.data.member)} · ${me.data.tenant.githubOrgLogin}`
    : list.length === 0
      ? "맡은 일이 없습니다."
      : stale > 0
        ? `맡은 일 중 ${stale}건이 7일 넘게 멈춰 있습니다.`
        : `진행 중 ${progress}건, 리뷰 중 ${review}건을 맡고 있습니다.`;

  return (
    <div className="flex flex-col gap-6">
      <PageHeader eyebrow={`${personName(me.data.member)} · ${me.data.tenant.githubOrgLogin}`} title="내 작업">
        {fact}
      </PageHeader>
      {issues.error && <ErrorBox error={issues.error} />}
      {issues.data ? (
        <div className="reveal [animation-delay:40ms]">
          <StatusColumns issues={issues.data} showAssignee={false} />
        </div>
      ) : (
        !issues.error && <Loading />
      )}
    </div>
  );
}
