import type { BoardIssue, Priority, Size, Status } from "./types";

// Screens never show Jira vocabulary; these are the words people see.
export const STATUS_ORDER: Status[] = ["todo", "in_progress", "in_review", "done"];

export const STATUS_LABEL: Record<Status, string> = {
  todo: "할 일",
  in_progress: "진행 중",
  in_review: "리뷰 중",
  done: "완료",
};

/** The only place status colours are bound to class names (docs/design.md section 6). */
export const STATUS_DOT: Record<Status, string> = {
  todo: "bg-status-todo",
  in_progress: "bg-status-progress",
  in_review: "bg-status-review",
  done: "bg-status-done",
};

export const STATUS_RULE: Record<Status, string> = {
  todo: "border-l-status-todo",
  in_progress: "border-l-status-progress",
  in_review: "border-l-status-review",
  done: "border-l-status-done",
};

export const CANCELED_LABEL = "취소";
export const CANCELED_DOT = "bg-status-cancel";

/** The API has no canceled status yet; once it sends one, it shows up as a folded group under 완료. */
export function isCanceled(issue: { status: string }): boolean {
  return issue.status === "canceled";
}

export const TYPE_LABEL: Record<string, string> = {
  Epic: "큰 목표",
  Feature: "기능",
  Task: "작업",
  Bug: "버그",
};

export const PRIORITY_LABEL: Record<Priority, string> = {
  urgent: "긴급",
  high: "높음",
  normal: "보통",
  low: "낮음",
};

export const SIZE_LABEL: Record<Size, string> = { S: "작음", M: "보통", L: "큼" };

export const STALE_DAYS = 7;

export function daysSince(iso: string): number {
  return Math.max(0, Math.floor((Date.now() - Date.parse(iso)) / 86_400_000));
}

export function relativeDays(iso: string): string {
  const d = daysSince(iso);
  return d === 0 ? "오늘" : `${d}일 전`;
}

/** "방금", "3시간 전", "2일 전" for activity lines. */
export function relativeTime(iso: string): string {
  const minutes = Math.max(0, Math.floor((Date.now() - Date.parse(iso)) / 60_000));
  if (minutes < 1) return "방금";
  if (minutes < 60) return `${minutes}분 전`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}시간 전`;
  return `${Math.floor(hours / 24)}일 전`;
}

export function isStale(issue: Pick<BoardIssue, "state" | "updatedAt">): boolean {
  return issue.state === "open" && daysSince(issue.updatedAt) >= STALE_DAYS;
}

export function personName(p: { displayName: string | null; githubLogin: string | null }): string {
  return p.displayName ?? p.githubLogin ?? "알 수 없음";
}

/** Korean names show the given name's first syllable (김하늘 → 하); others the first letter. */
export function initial(p: { displayName: string | null; githubLogin: string | null }): string {
  const name = personName(p).trim();
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

export function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("ko-KR", { month: "long", day: "numeric", weekday: "short" });
}

export function shortDate(iso: string): string {
  const d = new Date(iso);
  return `${String(d.getMonth() + 1).padStart(2, "0")}.${String(d.getDate()).padStart(2, "0")}`;
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
