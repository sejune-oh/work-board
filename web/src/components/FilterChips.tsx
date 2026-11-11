"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { type BoardFilters, type FilterKey, FILTER_KEYS } from "@/lib/filters";
import { useT } from "@/lib/i18n";
import { STATUS_DOT, STALE_DAYS } from "@/lib/labels";
import type { Status } from "@/lib/types";
import { Dot } from "./ui";

type Props = {
  filters: BoardFilters;
  personName?: (login: string) => string;
  sprintName?: (id: string) => string | undefined;
};

/** Active board filters as removable chips; nothing renders when no filter is on. */
export function FilterChips({ filters, personName = (l) => l, sprintName }: Props) {
  const t = useT();
  const f = t.filters;
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
          {t.status[s]}
        </>
      ),
      title: f.remove(t.status[s]),
      onRemove: () => removeStatus(s),
    })),
  ];
  if (filters.stale)
    chips.push({ key: "stale", label: f.stale(STALE_DAYS), title: f.removeStale, onRemove: () => removeKey("stale") });
  if (filters.doneThisWeek)
    chips.push({ key: "doneSince", label: f.doneThisWeek, title: f.remove(f.doneThisWeek), onRemove: () => removeKey("doneSince") });
  if (filters.person) {
    const name = personName(filters.person);
    chips.push({ key: "person", label: f.person(name), title: f.remove(name), onRemove: () => removeKey("person") });
  }
  if (filters.sprint) {
    const name = sprintName?.(filters.sprint) ?? f.sprintFallback;
    chips.push({ key: "sprint", label: f.sprint(name), title: f.remove(name), onRemove: () => removeKey("sprint") });
  }

  if (chips.length === 0) return null;

  return (
    <div className="flex flex-wrap items-center gap-2" aria-label={f.aria}>
      <span className="text-small text-muted">{f.label}</span>
      {chips.map((c) => (
        <button
          key={c.key}
          type="button"
          onClick={c.onRemove}
          title={c.title}
          aria-label={c.title}
          className="inline-flex h-8 cursor-pointer items-center gap-1.5 rounded-full bg-surface px-3 text-small font-medium text-ink shadow-[inset_0_0_0_1px_var(--wb-rule-strong)] transition-colors duration-[120ms] ease-tool hover:bg-hover"
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
          className="link cursor-pointer text-small text-ink-soft"
        >
          {f.clearAll}
        </button>
      )}
    </div>
  );
}
