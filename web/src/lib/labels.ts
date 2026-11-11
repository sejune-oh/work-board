import type { BoardIssue, Status } from "./types";

/** Board columns. Cancelled work has no column of its own; it folds under done. */
export const STATUS_ORDER: Status[] = ["todo", "in_progress", "in_review", "done"];

/** Every status a filter can name, in the order chips and menus list them. */
export const FILTER_STATUSES: Status[] = [...STATUS_ORDER, "cancelled"];

/** The only place status colours are bound to class names (docs/design.md section 6). */
export const STATUS_DOT: Record<Status, string> = {
  todo: "bg-status-todo",
  in_progress: "bg-status-progress",
  in_review: "bg-status-review",
  done: "bg-status-done",
  cancelled: "bg-status-cancel",
};

export function isCanceled(issue: { status: Status }): boolean {
  return issue.status === "cancelled";
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
  const groups: Record<Status, BoardIssue[]> = { todo: [], in_progress: [], in_review: [], done: [], cancelled: [] };
  for (const issue of issues) groups[issue.status].push(issue);
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

const LABEL_DARK_TEXT = "101317";
const LABEL_LIGHT_TEXT = "ffffff";

function luminance(hex: string): number {
  const [r, g, b] = [0, 2, 4].map((i) => {
    const c = parseInt(hex.slice(i, i + 2), 16) / 255;
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

/**
 * A GitHub label's own colour as the chip background, with whichever of near-black or white text
 * has the higher WCAG contrast on it. The background ignores the theme, so the text does too.
 * `null` when the colour is missing or malformed; the chip then uses the neutral style.
 */
export function labelStyle(color: string | null): { background: string; color: string } | null {
  if (!color || !/^[0-9a-f]{6}$/i.test(color)) return null;
  const l = luminance(color);
  const withLight = (luminance(LABEL_LIGHT_TEXT) + 0.05) / (l + 0.05);
  const withDark = (l + 0.05) / (luminance(LABEL_DARK_TEXT) + 0.05);
  return { background: `#${color}`, color: `#${withDark >= withLight ? LABEL_DARK_TEXT : LABEL_LIGHT_TEXT}` };
}

/** The URL if it is an absolute http(s) URL, else `null`, so data from the API never becomes a `javascript:` link. */
export function safeHttpUrl(url: string | null | undefined): string | null {
  if (!url) return null;
  try {
    const parsed = new URL(url);
    return parsed.protocol === "https:" || parsed.protocol === "http:" ? parsed.href : null;
  } catch {
    return null;
  }
}
