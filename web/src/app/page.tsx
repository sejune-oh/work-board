"use client";

import { ErrorBox, Loading } from "@/components/Feedback";
import { StatusColumns } from "@/components/StatusColumns";
import { useApi } from "@/lib/api";
import { personName } from "@/lib/labels";
import type { BoardIssue, Me } from "@/lib/types";

export default function MyWorkPage() {
  const me = useApi<Me>("/api/me");
  const login = me.data?.member.githubLogin;
  const issues = useApi<BoardIssue[]>(login ? `/api/issues?assignee=${encodeURIComponent(login)}` : null);

  if (me.error) return <ErrorBox error={me.error} />;
  if (!me.data) return <Loading />;

  return (
    <div className="flex flex-col gap-4">
      <div>
        <h1 className="text-xl font-bold">내 작업</h1>
        <p className="text-sm text-slate-500">
          {personName(me.data.member)} · {me.data.tenant.githubOrgLogin}
        </p>
      </div>
      {issues.error && <ErrorBox error={issues.error} />}
      {issues.data ? <StatusColumns issues={issues.data} showAssignee={false} /> : <Loading />}
    </div>
  );
}
