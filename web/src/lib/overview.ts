import type { Dict } from "./i18n/ko";
import { daysSince } from "./labels";
import type { Overview } from "./types";

/**
 * One clause of the headline. `nums` are the figures to emphasise; `parts` interleave text and
 * figure indexes. `and` joins it to a following clause, `end` closes the sentence.
 */
export type Clause = { parts: (string | number)[]; and: string; end: string };

const has = (n: number) => n > 0;

/**
 * Rules from docs/overview.md 3-1: the first matching clause leads, the second is joined to it,
 * everything else drops to the sub line. The wording lives in the dictionaries, not in the API.
 */
export function headlineClauses(o: Overview, dict: Dict): { lead: Clause[]; rest: Clause[] } {
  const t = o.attention.totals;
  const h = dict.overview.headline;
  const clauses: Clause[] = [];

  if (has(t.urgentNotStarted)) clauses.push(h.urgent(t.urgent, t.urgentNotStarted));
  if (has(t.reviewWaiting)) clauses.push(h.reviewWaiting(t.reviewWaiting));
  if (has(t.unassigned)) clauses.push(h.unassigned(t.unassigned));
  // counts.stale, not attention.totals.stale: the figure tile shows the same number.
  if (has(o.counts.stale)) clauses.push(h.stale(o.counts.stale));

  if (clauses.length === 0) return { lead: [h.allClear(o.counts.inProgress)], rest: [] };
  return { lead: clauses.slice(0, 2), rest: clauses.slice(2) };
}

export function clauseText(c: Clause): string {
  return c.parts.join("") + c.end;
}

/** Good news worth saying out loud on the sub line when a group is empty. */
export function clearNotes(o: Overview, priorityEnabled: boolean, dict: Dict): string[] {
  const t = o.attention.totals;
  const c = dict.overview.clear;
  const notes: string[] = [];
  if (priorityEnabled && t.urgent === 0) notes.push(c.urgent);
  if (t.reviewWaiting === 0) notes.push(c.reviewWaiting);
  if (o.counts.stale === 0) notes.push(c.stale);
  if (t.unassigned === 0) notes.push(c.unassigned);
  if (t.actionItemsWithoutIssue === 0) notes.push(c.actionItems);
  return notes;
}

export function isEmptyTeam(o: Overview): boolean {
  return o.counts.open === 0 && o.counts.doneThisWeek === 0;
}

export function oldestWaitDays(o: Overview): number | null {
  const waits = o.attention.reviewWaiting.map((r) => daysSince(r.pullRequest.waitingSince));
  return waits.length ? Math.max(...waits) : null;
}

/** `yyyy-MM-dd` as a local date, so the sprint calendar does not shift with the UTC offset. */
export function parseDay(day: string): Date {
  const [y, m, d] = day.split("-").map(Number);
  return new Date(y, m - 1, d);
}

export type CalendarDay = { key: string; past: boolean; today: boolean; weekend: boolean };

export function sprintCalendar(startDate: string, endDate: string, now = new Date()): CalendarDay[] {
  const start = parseDay(startDate);
  const end = parseDay(endDate);
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime();
  const days: CalendarDay[] = [];
  for (let d = new Date(start); d <= end && days.length < 62; d.setDate(d.getDate() + 1)) {
    const t = d.getTime();
    days.push({ key: String(t), past: t < today, today: t === today, weekend: d.getDay() === 0 || d.getDay() === 6 });
  }
  return days;
}
