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
      <span className={large ? "text-small font-semibold text-ink" : "hidden text-meta text-muted min-[1024px]:inline"}>{t.userPicker.label}</span>
      <select
        className={
          large
            ? "h-12 cursor-pointer rounded-item bg-surface px-3.5 text-body text-ink shadow-[inset_0_0_0_1px_var(--wb-rule-strong)] focus:shadow-[inset_0_0_0_2px_var(--wb-focus)] focus:outline-none"
            : "h-8 max-w-[6.5rem] cursor-pointer rounded-control bg-surface px-2 text-small font-medium text-ink shadow-elev-1 min-[640px]:max-w-[11rem]"
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
