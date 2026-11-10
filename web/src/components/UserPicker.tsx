"use client";

import { useEffect, useState } from "react";
import { getDevUser, setDevUser, useApi } from "@/lib/api";
import type { DevUser } from "@/lib/types";

/** Stand-in for GitHub login while everything runs locally. */
export function UserPicker() {
  const { data } = useApi<DevUser[]>("/api/dev/users");
  const [current, setCurrent] = useState<string | null>(null);

  useEffect(() => setCurrent(getDevUser()), []);

  return (
    <label className="flex items-center gap-2 text-sm">
      <span className="text-slate-500">개발용 사용자</span>
      <select
        className="rounded-md border border-slate-300 bg-white px-2 py-1 dark:border-slate-700 dark:bg-slate-900"
        value={current ?? ""}
        onChange={(e) => {
          const login = e.target.value || null;
          setCurrent(login);
          setDevUser(login);
        }}
      >
        <option value="">선택하세요</option>
        {data?.map((u) => (
          <option key={u.githubLogin} value={u.githubLogin}>
            {u.displayName ?? u.githubLogin} ({u.tenantLogin})
          </option>
        ))}
      </select>
    </label>
  );
}
