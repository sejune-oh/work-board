"use client";

import { type Dict, useFormat, useT } from "@/lib/i18n";
import { STATUS_DOT, initial, labelStyle, percent } from "@/lib/labels";
import type { IssueLabel, Status, StatusCounts } from "@/lib/types";

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

export function ExternalIcon({ size = 14 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden className="shrink-0">
      <path d="M9.5 2.5h4v4M13.5 2.5 7.5 8.5" strokeLinecap="round" strokeLinejoin="round" />
      <path d="M11.5 9.5v3a1 1 0 0 1-1 1h-7a1 1 0 0 1-1-1v-7a1 1 0 0 1 1-1h3" strokeLinecap="round" />
    </svg>
  );
}

/** Link to the original on GitHub, always in a new tab. */
export function GitHubLink({
  href,
  label,
  className = "",
  children,
}: {
  href: string;
  label: string;
  className?: string;
  children?: React.ReactNode;
}) {
  const t = useT();
  return (
    <a href={href} target="_blank" rel="noopener noreferrer" aria-label={label} title={children ? t.github.newTab : label} className={className}>
      {children}
      <ExternalIcon />
    </a>
  );
}

/** GitHub labels in their own colours; past `max`, a `+n` chip names the rest in its tooltip. */
export function LabelChips({ labels, max = 3 }: { labels: IssueLabel[]; max?: number }) {
  const t = useT();
  if (labels.length === 0) return null;
  const shown = labels.slice(0, max);
  const rest = labels.slice(max);
  const chip = "inline-flex h-5 max-w-[10rem] items-center truncate rounded-full px-2 text-meta font-medium";
  return (
    <ul className="flex min-w-0 flex-wrap items-center gap-1" aria-label={t.issueMeta.labels}>
      {shown.map((l) => {
        const style = labelStyle(l.color);
        return (
          <li
            key={l.id}
            className={`${chip} shadow-[inset_0_0_0_1px_var(--wb-ring)] ${style ? "" : "bg-sunken text-ink-soft"}`}
            style={style ?? undefined}
            title={l.name}
          >
            <span className="truncate">{l.name}</span>
          </li>
        );
      })}
      {rest.length > 0 && (
        <li className={`${chip} font-mono text-muted`} title={t.issueMeta.moreLabelsTitle(rest.map((l) => l.name).join(", "))}>
          {t.issueMeta.moreLabels(rest.length)}
        </li>
      )}
    </ul>
  );
}

export function MilestoneIcon() {
  return (
    <svg width="12" height="12" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden className="shrink-0">
      <path d="M8 2.5 13.5 8 8 13.5 2.5 8z" strokeLinejoin="round" />
    </svg>
  );
}

export function MilestoneTag({ title }: { title: string }) {
  const t = useT();
  return (
    <span className="inline-flex min-w-0 items-center gap-1 text-meta text-muted" title={`${t.issueMeta.milestone}: ${title}`}>
      <MilestoneIcon />
      <span className="sr-only">{t.issueMeta.milestone}</span>
      <span className="truncate">{title}</span>
    </span>
  );
}
