import type { BoardIssue, Status } from "./types";

// Screens never show Jira vocabulary; these are the words people see.
export const STATUS_ORDER: Status[] = ["todo", "in_progress", "in_review", "done"];

/** The only place status colours are bound to class names (docs/design.md section 6). */
export const STATUS_DOT: Record<Status, string> = {
  todo: "bg-status-todo",
  in_progress: "bg-status-progress",
  in_review: "bg-status-review",
  done: "bg-status-done",
};

export const CANCELED_DOT = "bg-status-cancel";

/** The API has no canceled status yet; once it sends one, it shows up as a folded group under 완료. */
export function isCanceled(issue: { status: string }): boolean {
  return issue.status === "canceled";
}

export const STALE_DAYS = 7;

export function daysSince(iso: string): number {
  return Math.max(0, Math.floor((Date.now() - Date.parse(iso)) / 86_400_000));
}

export function isStale(issue: Pick<BoardIssue, "state" | "updatedAt">): boolean {
  return issue.state === "open" && daysSince(issue.updatedAt) >= STALE_DAYS;
}

/** Prefer `useFormat().person`, which fills in the localized fallback. */
export function personName(p: { displayName: string | null; githubLogin: string | null }, unknown: string): string {
  return p.displayName ?? p.githubLogin ?? unknown;
}

/** Korean names show the given name's first syllable (김하늘 → 하); others the first letter. */
export function initial(displayName: string): string {
  const name = displayName.trim();
  if (/^[가-힣]{2,4}$/.test(name)) return name.length >= 3 ? name[1] : name[0];
  return (name[0] ?? "?").toUpperCase();
}

export function groupByStatus(issues: BoardIssue[]): Record<Status, BoardIssue[]> {
  const groups: Record<Status, BoardIssue[]> = { todo: [], in_progress: [], in_review: [], done: [] };
  for (const issue of issues) {
    if (isCanceled(issue)) continue;
    groups[issue.status].push(issue);
  }
  return groups;
}

export function percent(part: number, total: number): number {
  return total > 0 ? Math.round((part / total) * 100) : 0;
}

export function median(values: number[]): number {
  if (values.length === 0) return 0;
  const sorted = [...values].sort((a, b) => a - b);
  const mid = Math.floor(sorted.length / 2);
  return sorted.length % 2 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
}

export const CROWDED_MIN = 4;

/** "몰림": at least 4 in-progress-category issues and at least twice the team median. */
export function isCrowded(active: number, teamActive: number[]): boolean {
  return active >= CROWDED_MIN && active >= 2 * median(teamActive);
}
