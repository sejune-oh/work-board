import type { BoardIssue, Priority, Size, Status } from "./types";

// Screens never show Jira vocabulary; these are the words people see.
export const STATUS_ORDER: Status[] = ["todo", "in_progress", "in_review", "done"];

export const STATUS_LABEL: Record<Status, string> = {
  todo: "할 일",
  in_progress: "진행 중",
  in_review: "리뷰 중",
  done: "완료",
};

export const STATUS_DOT: Record<Status, string> = {
  todo: "bg-slate-400",
  in_progress: "bg-blue-500",
  in_review: "bg-amber-500",
  done: "bg-emerald-500",
};

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

export const PRIORITY_STYLE: Record<Priority, string> = {
  urgent: "bg-red-100 text-red-700 dark:bg-red-950 dark:text-red-300",
  high: "bg-orange-100 text-orange-700 dark:bg-orange-950 dark:text-orange-300",
  normal: "bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-300",
  low: "bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400",
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

export function personName(p: { displayName: string | null; githubLogin: string | null }): string {
  return p.displayName ?? p.githubLogin ?? "알 수 없음";
}

export function groupByStatus(issues: BoardIssue[]): Record<Status, BoardIssue[]> {
  const groups: Record<Status, BoardIssue[]> = { todo: [], in_progress: [], in_review: [], done: [] };
  for (const issue of issues) groups[issue.status].push(issue);
  return groups;
}

export function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("ko-KR", { month: "long", day: "numeric", weekday: "short" });
}
