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
 * everything else drops to the sub line. The wording lives here, not in the API.
 */
export function headlineClauses(o: Overview): { lead: Clause[]; rest: Clause[] } {
  const t = o.attention.totals;
  const notStarted = o.attention.urgent.filter((u) => u.notStarted).length;
  const clauses: Clause[] = [];

  if (has(notStarted)) clauses.push({ parts: [`긴급 ${t.urgent}건 중 `, notStarted, "건은 아직 시작 전"], and: "이고, ", end: "입니다." });
  if (has(t.reviewWaiting)) clauses.push({ parts: ["리뷰를 기다리는 PR이 ", t.reviewWaiting, "건 있"], and: "고, ", end: "습니다." });
  if (has(t.unassigned)) clauses.push({ parts: ["담당자 없는 일이 ", t.unassigned, "건 있"], and: "고, ", end: "습니다." });
  if (has(t.stale)) clauses.push({ parts: ["", t.stale, "건이 7일 넘게 멈춰 있"], and: "고, ", end: "습니다." });

  if (clauses.length === 0) {
    return { lead: [{ parts: ["막힌 곳 없이 ", o.counts.inProgress, "건이 진행 중"], and: "이고, ", end: "입니다." }], rest: [] };
  }
  return { lead: clauses.slice(0, 2), rest: clauses.slice(2) };
}

export function clauseText(c: Clause): string {
  return c.parts.join("") + c.end;
}

/** Good news worth saying out loud on the sub line when a group is empty. */
export function clearNotes(o: Overview, priorityEnabled: boolean): string[] {
  const t = o.attention.totals;
  const notes: string[] = [];
  if (priorityEnabled && t.urgent === 0) notes.push("긴급한 일 없음");
  if (t.reviewWaiting === 0) notes.push("리뷰 기다리는 PR 없음");
  if (t.stale === 0) notes.push("7일 넘게 멈춘 일 없음");
  if (t.unassigned === 0) notes.push("담당자 없는 일 없음");
  if (t.actionItemsWithoutIssue === 0) notes.push("이슈 없는 회의 할 일 없음");
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

/** "오늘 · 11월 10일", "어제 · 11월 9일", "11월 8일". */
export function dayLabel(iso: string, now = new Date()): string {
  const d = new Date(iso);
  const base = `${d.getMonth() + 1}월 ${d.getDate()}일`;
  const startOf = (x: Date) => new Date(x.getFullYear(), x.getMonth(), x.getDate()).getTime();
  const diff = Math.round((startOf(now) - startOf(d)) / 86_400_000);
  if (diff === 0) return `오늘 · ${base}`;
  if (diff === 1) return `어제 · ${base}`;
  return base;
}
