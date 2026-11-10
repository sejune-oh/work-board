"use client";

import { type Dict, useFormat, useT } from "@/lib/i18n";
import { STATUS_DOT, initial, percent } from "@/lib/labels";
import type { Status, StatusCounts } from "@/lib/types";

type Person = { displayName: string | null; githubLogin: string | null };

export function Avatar({ person, size = "md", ghost = false }: { person: Person; size?: "sm" | "md" | "lg"; ghost?: boolean }) {
  const dims = size === "sm" ? "h-5 w-5 text-[10px]" : size === "lg" ? "h-8 w-8 text-small" : "h-6 w-6 text-[11px]";
  const fmt = useFormat();
  const tone = ghost ? "shadow-[inset_0_0_0_1px_var(--wb-rule-strong)] text-muted" : "bg-ink text-on-ink";
  return (
    <span aria-hidden className={`grid shrink-0 place-items-center rounded-full font-semibold ${dims} ${tone}`}>
      {initial(fmt.person(person))}
    </span>
  );
}

/** Attention mark. `fill` is reserved for the strongest signal (urgent) to keep painted areas few. */
export function Flag({ children, fill = false, className = "" }: { children: React.ReactNode; fill?: boolean; className?: string }) {
  return <span className={`flag ${fill ? "flag-fill" : ""} ${className}`}>{children}</span>;
}

export function Kbd({ children, className = "" }: { children: React.ReactNode; className?: string }) {
  return <kbd className={`kbd ${className}`}>{children}</kbd>;
}

export function Dot({ className }: { className: string }) {
  return <span aria-hidden className={`inline-block h-2 w-2 shrink-0 rounded-full ${className}`} />;
}

export function StatusBadge({ status }: { status: Status }) {
  const t = useT();
  return (
    <span className="inline-flex items-center gap-1.5 whitespace-nowrap text-small text-ink-soft">
      <Dot className={STATUS_DOT[status]} />
      {t.status[status]}
    </span>
  );
}

/** Context line, the screen title, then the one most important fact in plain words. */
export function PageHeader({ meta, title, children }: { meta?: React.ReactNode; title: React.ReactNode; children?: React.ReactNode }) {
  return (
    <header className="reveal flex max-w-[860px] flex-col gap-2">
      {meta && <p className="text-small text-muted">{meta}</p>}
      <h1 className="text-title font-semibold">{title}</h1>
      {children && <div className="text-body text-ink-soft">{children}</div>}
    </header>
  );
}

export function Count({ n }: { n: number }) {
  return <span className="font-mono text-meta font-normal text-muted">{n}</span>;
}

/** Heading for a section that is not a card: title, count, a short hint, and a link on the right. */
export function SectionHead({
  title,
  count,
  hint,
  id,
  action,
}: {
  title: React.ReactNode;
  count?: number;
  hint?: string;
  id?: string;
  action?: React.ReactNode;
}) {
  return (
    <div className="flex min-h-8 flex-wrap items-center gap-x-3 gap-y-1">
      <h2 id={id} className="text-heading font-semibold">
        {title}
      </h2>
      {count !== undefined && <Count n={count} />}
      {hint && <span className="truncate text-small text-muted">{hint}</span>}
      {action && <div className="ml-auto whitespace-nowrap text-small text-muted">{action}</div>}
    </div>
  );
}

/** A panel with a head (title, count, hint, link) and no line under it. Most sections live in one. */
export function Card({
  title,
  count,
  hint,
  id,
  action,
  className = "",
  children,
}: {
  title: React.ReactNode;
  count?: number;
  hint?: string;
  id?: string;
  action?: React.ReactNode;
  className?: string;
  children: React.ReactNode;
}) {
  return (
    <div className={`card ${className}`}>
      <div className="flex min-h-[60px] items-center gap-3 px-5 pb-2 pt-4">
        <h2 id={id} className="whitespace-nowrap text-heading font-semibold">
          {title}
        </h2>
        {count !== undefined && <Count n={count} />}
        {hint && <span className="min-w-0 truncate text-small text-muted">{hint}</span>}
        {action && <div className="ml-auto flex shrink-0 items-center gap-1.5 whitespace-nowrap text-small text-muted">{action}</div>}
      </div>
      {children}
    </div>
  );
}

/** Done, review, progress, todo from the left, always with the numbers beside it. */
export function ProgressBar({ counts, label }: { counts: StatusCounts; label?: string }) {
  const t = useT();
  const total = counts.todo + counts.inProgress + counts.inReview + counts.done;
  if (total === 0) return <p className="text-small text-muted">{t.progress.empty}</p>;
  const segs: [number, string][] = [
    [counts.done, "bg-status-done"],
    [counts.inReview, "bg-status-review"],
    [counts.inProgress, "bg-status-progress"],
    [counts.todo, "bg-status-todo opacity-50"],
  ];
  const aria = label ?? t.progress.aria(counts.done, counts.inReview, counts.inProgress, counts.todo);
  return (
    <div className="bar" role="img" aria-label={aria}>
      {segs.map(([n, cls]) => (n > 0 ? <span key={cls} className={cls} style={{ width: `${(n / total) * 100}%` }} /> : null))}
    </div>
  );
}

export function progressLine(counts: StatusCounts, t: Dict): string {
  const total = counts.todo + counts.inProgress + counts.inReview + counts.done;
  return t.progress.line(counts.done, total, percent(counts.done, total));
}

export function MonoRef({ repo, number }: { repo: string; number: number }) {
  return (
    <span className="whitespace-nowrap font-mono text-meta">
      {repo} #{number}
    </span>
  );
}
