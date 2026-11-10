"use client";

import { useEffect, useState } from "react";
import { DEV_USER_EVENT, type DevIdentity, getDevIdentity, setDevIdentity, useApi } from "@/lib/api";
import { useT } from "@/lib/i18n";
import type { DevUser } from "@/lib/types";

const optionValue = (u: DevUser) => `${u.tenantId}/${u.githubLogin}`;

/** Stand-in for GitHub login while everything runs locally. */
export function UserPicker({ large = false }: { large?: boolean }) {
  const t = useT();
  const { data, error } = useApi<DevUser[]>("/api/dev/users");
  const [current, setCurrent] = useState<DevIdentity | null>(null);

  useEffect(() => {
    const sync = () => setCurrent(getDevIdentity());
    sync();
    window.addEventListener(DEV_USER_EVENT, sync);
    return () => window.removeEventListener(DEV_USER_EVENT, sync);
  }, []);

  const selected = data?.find((u) => u.githubLogin === current?.login && u.tenantId === current?.tenantId);

  return (
    <label className={large ? "flex flex-col gap-2" : "flex items-center gap-2 text-ui"}>
      <span className={large ? "font-mono text-meta text-muted" : "hidden text-meta text-muted sm:inline"}>{t.userPicker.label}</span>
      <select
        className={
          large
            ? "h-10 cursor-pointer rounded-control border border-rule-strong bg-surface px-3 text-body text-ink focus:border-focus"
            : "h-[30px] max-w-[8rem] cursor-pointer rounded-control border border-rule bg-surface px-1.5 text-ui font-medium text-ink hover:border-rule-strong sm:max-w-[11rem]"
        }
        value={selected ? optionValue(selected) : ""}
        onChange={(e) => {
          const user = data?.find((u) => optionValue(u) === e.target.value);
          setDevIdentity(user ? { login: user.githubLogin, tenantId: user.tenantId } : null);
        }}
      >
        <option value="">{error ? t.userPicker.loadFailed : t.userPicker.placeholder}</option>
        {data?.map((u) => (
          <option key={optionValue(u)} value={optionValue(u)}>
            {u.displayName ?? u.githubLogin} ({u.tenantName})
          </option>
        ))}
      </select>
    </label>
  );
}
