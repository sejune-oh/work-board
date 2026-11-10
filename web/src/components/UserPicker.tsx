"use client";

import { useEffect, useState } from "react";
import { getDevUser, setDevUser, useApi } from "@/lib/api";
import type { DevUser } from "@/lib/types";

/** Stand-in for GitHub login while everything runs locally. */
export function UserPicker({ large = false }: { large?: boolean }) {
  const { data, error } = useApi<DevUser[]>("/api/dev/users");
  const [current, setCurrent] = useState<string | null>(null);

  useEffect(() => {
    const sync = () => setCurrent(getDevUser());
    sync();
    window.addEventListener("workboard:dev-user", sync);
    return () => window.removeEventListener("workboard:dev-user", sync);
  }, []);

  return (
    <label className={large ? "flex flex-col gap-2" : "flex items-center gap-2 text-sm"}>
      <span className={large ? "text-sm font-semibold" : "text-slate-500"}>개발용 사용자</span>
      <select
        className={
          large
            ? "rounded-lg border-2 border-blue-500 bg-white px-3 py-2.5 text-base dark:bg-slate-900"
            : "rounded-md border border-slate-300 bg-white px-2 py-1 dark:border-slate-700 dark:bg-slate-900"
        }
        value={current ?? ""}
        onChange={(e) => {
          const login = e.target.value || null;
          setCurrent(login);
          setDevUser(login);
        }}
      >
        <option value="">{error ? "사용자 목록을 불러오지 못했습니다" : "선택하세요"}</option>
        {data?.map((u) => (
          <option key={u.githubLogin} value={u.githubLogin}>
            {u.displayName ?? u.githubLogin} ({u.tenantLogin})
          </option>
        ))}
      </select>
    </label>
  );
}
