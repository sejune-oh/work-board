import { STATUS_ORDER, isStale } from "./labels";
import type { BoardIssue, Status } from "./types";

/** Board filters carried in the URL so the overview can link straight into a filtered board. */
export type BoardFilters = {
  statuses: Status[];
  stale: boolean;
  person: string | null;
  sprint: string | null;
};

type Params = { get(name: string): string | null };

export const FILTER_KEYS = ["status", "stale", "person", "sprint"] as const;
export type FilterKey = (typeof FILTER_KEYS)[number];

export function readFilters(params: Params): BoardFilters {
  const statuses = (params.get("status") ?? "")
    .split(",")
    .map((s) => s.trim())
    .filter((s): s is Status => (STATUS_ORDER as string[]).includes(s));
  const stale = params.get("stale");
  return {
    statuses: STATUS_ORDER.filter((s) => statuses.includes(s)),
    stale: stale === "1" || stale === "true",
    person: params.get("person") || null,
    sprint: params.get("sprint") || null,
  };
}

export function hasFilters(f: BoardFilters): boolean {
  return f.statuses.length > 0 || f.stale || f.person !== null || f.sprint !== null;
}

/** Query string for `GET /api/issues`, including `extra` pairs such as `projectId` or `scope`. */
export function issuesQuery(f: BoardFilters, extra: Record<string, string> = {}): string {
  const q = new URLSearchParams(extra);
  if (f.statuses.length) q.set("status", f.statuses.join(","));
  if (f.stale) q.set("stale", "true");
  if (f.person) q.set("person", f.person);
  if (f.sprint) q.set("sprint", f.sprint);
  const s = q.toString();
  return s ? `/api/issues?${s}` : "/api/issues";
}

/** The server filters too; this keeps the board right if it is talking to an older API. */
export function applyFilters(issues: BoardIssue[], f: BoardFilters): BoardIssue[] {
  return issues.filter(
    (i) =>
      (f.statuses.length === 0 || f.statuses.includes(i.status)) &&
      (!f.stale || isStale(i)) &&
      (!f.person || i.assignees.some((a) => a.githubLogin === f.person)) &&
      (!f.sprint || i.sprint?.id === f.sprint),
  );
}
