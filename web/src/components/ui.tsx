import { STATUS_DOT, STATUS_LABEL, initial, percent } from "@/lib/labels";
import type { Status, StatusCounts } from "@/lib/types";

type Person = { displayName: string | null; githubLogin: string | null };

export function Avatar({ person, size = "md", ghost = false }: { person: Person; size?: "sm" | "md" | "lg"; ghost?: boolean }) {
  const dims = size === "sm" ? "h-5 w-5 text-[10.5px]" : size === "lg" ? "h-[34px] w-[34px] text-sm" : "h-[26px] w-[26px] text-xs";
  const tone = ghost ? "border border-dashed border-muted text-muted" : "bg-ink-soft text-paper";
  return (
    <span aria-hidden className={`grid shrink-0 place-items-center rounded-full font-semibold ${dims} ${tone}`}>
      {initial(person)}
    </span>
  );
}

/** Attention mark. `fill` is reserved for the strongest signal (긴급) to keep painted areas few. */
export function Stamp({ children, fill = false, className = "" }: { children: React.ReactNode; fill?: boolean; className?: string }) {
  return <span className={`stamp ${fill ? "stamp-fill" : ""} ${className}`}>{children}</span>;
}

export function Dot({ className }: { className: string }) {
  return <span aria-hidden className={`inline-block h-2 w-2 shrink-0 rounded-full ${className}`} />;
}

export function StatusBadge({ status }: { status: Status }) {
  return (
    <span className="inline-flex items-center gap-1.5 whitespace-nowrap text-meta text-ink-soft">
      <Dot className={STATUS_DOT[status]} />
      {STATUS_LABEL[status]}
    </span>
  );
}

export function PageHeader({ eyebrow, title, children }: { eyebrow?: string; title: React.ReactNode; children?: React.ReactNode }) {
  return (
    <header className="reveal border-b-2 border-rule-strong pb-3">
      {eyebrow && <p className="eyebrow mb-1 text-muted">{eyebrow}</p>}
      <h1 className="font-display text-title font-semibold tracking-[-0.01em]">{title}</h1>
      {children && <div className="mt-1 text-ui text-ink-soft">{children}</div>}
    </header>
  );
}

export function SectionHead({ eyebrow, title, id, action }: { eyebrow?: string; title: React.ReactNode; id?: string; action?: React.ReactNode }) {
  return (
    <div className="mb-3.5 flex items-baseline justify-between gap-3 border-t-2 border-rule-strong pt-2.5">
      <div>
        {eyebrow && <span className="eyebrow mb-0.5 block text-muted">{eyebrow}</span>}
        <h2 id={id} className="font-display text-[19px] font-semibold tracking-[-0.01em]">
          {title}
        </h2>
      </div>
      {action && <div className="whitespace-nowrap text-[13px] text-ink-soft">{action}</div>}
    </div>
  );
}


/** Done, review, progress, todo from the left, always with the numbers beside it. */
export function ProgressBar({ counts, thick = false, label }: { counts: StatusCounts; thick?: boolean; label?: string }) {
  const total = counts.todo + counts.inProgress + counts.inReview + counts.done;
  if (total === 0) return <p className="text-meta text-muted">아직 일이 없습니다</p>;
  const segs: [number, string][] = [
    [counts.done, "bg-status-done"],
    [counts.inReview, "bg-status-review"],
    [counts.inProgress, "bg-status-progress"],
    [counts.todo, "bg-status-todo opacity-55"],
  ];
  const aria =
    label ?? `완료 ${counts.done}, 리뷰 중 ${counts.inReview}, 진행 중 ${counts.inProgress}, 할 일 ${counts.todo}`;
  return (
    <div className={`bar ${thick ? "!h-2.5" : ""}`} role="img" aria-label={aria}>
      {segs.map(([n, cls]) => (n > 0 ? <span key={cls} className={cls} style={{ width: `${(n / total) * 100}%` }} /> : null))}
    </div>
  );
}

export function progressLine(counts: StatusCounts): string {
  const total = counts.todo + counts.inProgress + counts.inReview + counts.done;
  return `완료 ${counts.done} / ${total} · ${percent(counts.done, total)}%`;
}

export function MonoRef({ repo, number }: { repo: string; number: number }) {
  return (
    <span className="whitespace-nowrap font-mono text-meta tracking-[-0.01em]">
      {repo} #{number}
    </span>
  );
}
