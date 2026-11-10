import { STATUS_DOT, STATUS_LABEL, initial, percent } from "@/lib/labels";
import type { Status, StatusCounts } from "@/lib/types";

type Person = { displayName: string | null; githubLogin: string | null };

export function Avatar({ person, size = "md", ghost = false }: { person: Person; size?: "sm" | "md" | "lg"; ghost?: boolean }) {
  const dims = size === "sm" ? "h-[18px] w-[18px] text-[10px]" : size === "lg" ? "h-7 w-7 text-xs" : "h-[22px] w-[22px] text-[11px]";
  const tone = ghost ? "border border-dashed border-muted text-muted" : "bg-ink-soft text-surface";
  return (
    <span aria-hidden className={`grid shrink-0 place-items-center rounded-full font-semibold ${dims} ${tone}`}>
      {initial(person)}
    </span>
  );
}

/** Attention mark. `fill` is reserved for the strongest signal (긴급) to keep painted areas few. */
export function Flag({ children, fill = false, className = "" }: { children: React.ReactNode; fill?: boolean; className?: string }) {
  return <span className={`flag ${fill ? "flag-fill" : ""} ${className}`}>{children}</span>;
}

export function Kbd({ children, className = "" }: { children: React.ReactNode; className?: string }) {
  return <kbd className={`kbd ${className}`}>{children}</kbd>;
}

export function Dot({ className }: { className: string }) {
  return <span aria-hidden className={`inline-block h-[7px] w-[7px] shrink-0 rounded-full ${className}`} />;
}

export function StatusBadge({ status }: { status: Status }) {
  return (
    <span className="inline-flex items-center gap-1.5 whitespace-nowrap text-[12.5px] text-ink-soft">
      <Dot className={STATUS_DOT[status]} />
      {STATUS_LABEL[status]}
    </span>
  );
}

/** Mono context line, the screen title, then the one most important fact in plain words. */
export function PageHeader({ meta, title, children }: { meta?: React.ReactNode; title: React.ReactNode; children?: React.ReactNode }) {
  return (
    <header className="reveal">
      {meta && <p className="font-mono text-meta text-muted">{meta}</p>}
      <h1 className="mt-1 text-title font-semibold tracking-[-0.01em]">{title}</h1>
      {children && <div className="mt-0.5 text-body text-ink-soft">{children}</div>}
    </header>
  );
}

export function Count({ n }: { n: number }) {
  return <span className="rounded-[3px] border border-rule px-[5px] font-mono text-[11px] font-normal text-muted">{n}</span>;
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
    <div className="mb-2 flex min-h-8 items-center gap-2 border-b border-rule pb-2">
      <h2 id={id} className="text-heading font-semibold">
        {title}
      </h2>
      {count !== undefined && <Count n={count} />}
      {hint && <span className="truncate text-meta text-muted">{hint}</span>}
      {action && <div className="ml-auto whitespace-nowrap text-meta text-muted">{action}</div>}
    </div>
  );
}

/** A white panel with a 40px head. Most overview sections live in one. */
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
      <div className="flex h-10 items-center gap-2 border-b border-rule px-3.5">
        <h2 id={id} className="whitespace-nowrap text-heading font-semibold">
          {title}
        </h2>
        {count !== undefined && <Count n={count} />}
        {hint && <span className="truncate text-meta text-muted">{hint}</span>}
        {action && <div className="ml-auto flex shrink-0 items-center gap-1.5 whitespace-nowrap text-meta text-muted">{action}</div>}
      </div>
      {children}
    </div>
  );
}

/** Done, review, progress, todo from the left, always with the numbers beside it. */
export function ProgressBar({ counts, label }: { counts: StatusCounts; label?: string }) {
  const total = counts.todo + counts.inProgress + counts.inReview + counts.done;
  if (total === 0) return <p className="text-meta text-muted">아직 일이 없습니다</p>;
  const segs: [number, string][] = [
    [counts.done, "bg-status-done"],
    [counts.inReview, "bg-status-review"],
    [counts.inProgress, "bg-status-progress"],
    [counts.todo, "bg-status-todo opacity-50"],
  ];
  const aria =
    label ?? `완료 ${counts.done}, 리뷰 중 ${counts.inReview}, 진행 중 ${counts.inProgress}, 할 일 ${counts.todo}`;
  return (
    <div className="bar" role="img" aria-label={aria}>
      {segs.map(([n, cls]) => (n > 0 ? <span key={cls} className={cls} style={{ width: `${(n / total) * 100}%` }} /> : null))}
    </div>
  );
}

export function progressLine(counts: StatusCounts): string {
  const total = counts.todo + counts.inProgress + counts.inReview + counts.done;
  return `완료 ${counts.done}/${total} · ${percent(counts.done, total)}%`;
}

export function MonoRef({ repo, number }: { repo: string; number: number }) {
  return (
    <span className="whitespace-nowrap font-mono text-meta">
      {repo} #{number}
    </span>
  );
}
