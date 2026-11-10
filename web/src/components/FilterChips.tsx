"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { type BoardFilters, type FilterKey, FILTER_KEYS } from "@/lib/filters";
import { STATUS_DOT, STATUS_LABEL, STALE_DAYS } from "@/lib/labels";
import type { Status } from "@/lib/types";
import { Dot } from "./ui";

type Props = {
  filters: BoardFilters;
  personName?: (login: string) => string;
  sprintName?: (id: string) => string;
};

/** Active board filters as removable chips; nothing renders when no filter is on. */
export function FilterChips({ filters, personName = (l) => l, sprintName = () => "주기" }: Props) {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();

  const replace = (mutate: (next: URLSearchParams) => void) => {
    const next = new URLSearchParams(params.toString());
    mutate(next);
    const qs = next.toString();
    router.replace(qs ? `${pathname}?${qs}` : pathname, { scroll: false });
  };
  const removeKey = (key: FilterKey) => replace((n) => n.delete(key));
  const removeStatus = (s: Status) =>
    replace((n) => {
      const rest = filters.statuses.filter((x) => x !== s);
      if (rest.length) n.set("status", rest.join(","));
      else n.delete("status");
    });

  const chips: { key: string; label: React.ReactNode; title: string; onRemove: () => void }[] = [
    ...filters.statuses.map((s) => ({
      key: `status-${s}`,
      label: (
        <>
          <Dot className={STATUS_DOT[s]} />
          {STATUS_LABEL[s]}
        </>
      ),
      title: `${STATUS_LABEL[s]} 필터 빼기`,
      onRemove: () => removeStatus(s),
    })),
  ];
  if (filters.stale)
    chips.push({ key: "stale", label: `${STALE_DAYS}일 넘게 멈춤`, title: "멈춤 필터 빼기", onRemove: () => removeKey("stale") });
  if (filters.doneThisWeek)
    chips.push({ key: "doneSince", label: "이번 주 완료", title: "이번 주 완료 필터 빼기", onRemove: () => removeKey("doneSince") });
  if (filters.person) {
    const name = personName(filters.person);
    chips.push({ key: "person", label: `담당 ${name}`, title: `${name} 필터 빼기`, onRemove: () => removeKey("person") });
  }
  if (filters.sprint) {
    const name = sprintName(filters.sprint);
    chips.push({ key: "sprint", label: `주기 ${name}`, title: `${name} 필터 빼기`, onRemove: () => removeKey("sprint") });
  }

  if (chips.length === 0) return null;

  return (
    <div className="flex flex-wrap items-center gap-2" aria-label="적용된 필터">
      <span className="eyebrow text-muted">필터</span>
      {chips.map((c) => (
        <button
          key={c.key}
          type="button"
          onClick={c.onRemove}
          title={c.title}
          aria-label={c.title}
          className="inline-flex h-7 cursor-pointer items-center gap-1.5 rounded-card border border-rule bg-surface px-2 text-meta font-medium text-ink transition-colors duration-[120ms] ease-paper hover:border-ink-soft"
        >
          {c.label}
          <span aria-hidden className="text-muted">
            ✕
          </span>
        </button>
      ))}
      {chips.length > 1 && (
        <button
          type="button"
          onClick={() => replace((n) => FILTER_KEYS.forEach((k) => n.delete(k)))}
          className="link cursor-pointer text-meta text-ink-soft"
        >
          모두 지우기
        </button>
      )}
    </div>
  );
}
