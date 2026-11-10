"use client";

import { ErrorBox, Loading } from "@/components/Feedback";
import { StatusColumns } from "@/components/StatusColumns";
import { PageHeader } from "@/components/ui";
import { useApi } from "@/lib/api";
import { useFormat, useT } from "@/lib/i18n";
import { isStale } from "@/lib/labels";
import { useMe } from "@/lib/me";
import type { BoardIssue } from "@/lib/types";

export default function MyWorkPage() {
  const me = useMe();
  const t = useT();
  const fmt = useFormat();
  const login = me.data?.member.githubLogin;
  const issues = useApi<BoardIssue[]>(login ? `/api/issues?assignee=${encodeURIComponent(login)}` : null);

  if (me.error) return <ErrorBox error={me.error} />;
  if (!me.data) return <Loading />;

  const list = issues.data ?? [];
  const review = list.filter((i) => i.status === "in_review").length;
  const progress = list.filter((i) => i.status === "in_progress").length;
  const stale = list.filter(isStale).length;
  const meta = `${fmt.person(me.data.member)} · ${me.data.tenant.githubOrgLogin}`;
  const fact = !issues.data ? meta : list.length === 0 ? t.me.none : stale > 0 ? t.me.stale(stale) : t.me.summary(progress, review);

  return (
    <div className="flex flex-col gap-4">
      <PageHeader meta={meta} title={t.me.title}>
        {fact}
      </PageHeader>
      {issues.error && <ErrorBox error={issues.error} />}
      {issues.data ? (
        <div className="reveal mt-2 [animation-delay:30ms]">
          <StatusColumns issues={issues.data} showAssignee={false} />
        </div>
      ) : (
        !issues.error && <Loading />
      )}
    </div>
  );
}
