"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useCallback } from "react";

/** The issue detail panel is driven by `?issue=<id>` so any screen can open it and links can be shared. */
export function useIssuePanel() {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();

  const setIssue = useCallback(
    (id: string | null) => {
      const next = new URLSearchParams(params.toString());
      if (id) next.set("issue", id);
      else next.delete("issue");
      const qs = next.toString();
      router.push(qs ? `${pathname}?${qs}` : pathname, { scroll: false });
    },
    [params, pathname, router],
  );
  const close = useCallback(() => setIssue(null), [setIssue]);

  return { issueId: params.get("issue"), open: setIssue, close };
}
