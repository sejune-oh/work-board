# API contract (local skeleton)

Base URL: `http://localhost:5080`. All JSON is camelCase. Times are ISO 8601 UTC strings.

## Identity

Logged-in requests send `Authorization: Bearer <Supabase access token>`. When the person belongs to
several tenants they also send `X-Tenant: <tenantId>`.

- The API validates signature, issuer (`{SupabaseUrl}/auth/v1`), audience `authenticated` and
  lifetime. Keys come from `{SupabaseUrl}/auth/v1/.well-known/jwks.json`, or from the shared secret
  when `Auth:Supabase:JwtSecret` is set (legacy HS256 projects).
- Token `sub` is the Supabase auth user id. On first sight the API calls the Supabase Admin API
  (`GET /auth/v1/admin/users/{sub}`, service role key), takes the `github` identity's provider id and
  stores `auth_user_id -> github_user_id` in `auth_links` (global table, `workboard_sync` only).
  Login, display name and avatar (`user_name`/`preferred_username`, `full_name`/`name`, `avatar_url`) are read
  from the same identity's `identity_data`, only when a tenant is created. `user_metadata` is never used. Members are found by `github_user_id` across all tenants.
- One membership: `X-Tenant` is optional and ignored.
- Several memberships: `X-Tenant` must equal one of them.

Errors, JSON `{"error": "<code>"}`:

| Status | Code | When |
| --- | --- | --- |
| 401 | `unauthenticated` | no token, bad signature, wrong issuer or audience, expired, no GitHub identity |
| 401 | `tenant_required` | several memberships and `X-Tenant` missing or not one of them. Body also has `tenants`: `[{"id", "name"}]`, only the caller's own memberships |
| 403 | `no_membership` | valid login with a GitHub identity, but no membership in any tenant |

Development only, when there is no `Authorization` header: `X-Dev-User: <githubLogin>` and optional
`X-Dev-Tenant: <tenantId>` still work (same tenant rules, unknown login or no membership is
`401 unauthenticated`). With an `Authorization` header the dev headers are ignored. Outside
Development they are always ignored.

CORS allows the `Authorization` and `X-Tenant` request headers.

## Tenant isolation

Every request that touches tenant data runs inside a transaction on the `workboard_app` role and
first calls `select set_config('app.tenant_id', @tenantId, true)`. RLS policies in
`db/init/001_schema.sql` then hide other tenants' rows. Never query tenant tables on the
`workboard_sync` role from a user request.

## Endpoints

| Method | Path | Auth | Returns |
| --- | --- | --- | --- |
| GET | `/api/health` | none | `{"status":"ok"}` |
| GET | `/api/dev/users` | none (Development only) | `DevUser[]`, one entry per membership |
| POST | `/api/tenants` | sign-in only, no membership needed | `201 CreateTenantResult`, see below |
| GET | `/api/me` | dev user | `Me` |
| GET | `/api/members` | dev user | `Member[]` |
| GET | `/api/projects` | dev user | `Project[]` |
| GET | `/api/connections` | dev user | `GithubConnection[]`, active first |
| POST | `/api/connections/github` | dev user, admin | `GithubConnection` (`201` new, `200` existing updated) |
| GET | `/api/overview` | dev user | `Overview` |
| GET | `/api/issues` | dev user | `BoardIssue[]` |
| GET | `/api/issues/{id}` | dev user | `IssueDetail` or 404 |
| GET | `/api/meeting-notes` | dev user | `MeetingNoteSummary[]` |
| GET | `/api/meeting-notes/{id}` | dev user | `MeetingNote` or 404 |
| GET | `/api/search?q=` | dev user | `SearchResult` |

`POST /api/tenants` creates a company for a signed-in person (bearer token, or `X-Dev-User` with a known
login in Development) and makes them its `admin`. Body `{"name": "<string>"}`; the name is trimmed and must be
1..80 characters without control or invisible format characters (zero-width, bidi overrides); the request must be `application/json`. One transaction on the `workboard_sync` role inserts the tenant,
its `tenant_settings` row (all flags off) and the member (GitHub id, login, display name and avatar from the
GitHub identity). Response `201 {"tenant": {"id", "name"}, "member": Member}`; send `X-Tenant: <tenant.id>`
afterwards. Errors: `401 unauthenticated` (no valid sign-in, or the GitHub identity has no login),
`400 invalid_name` (also for a body that is not JSON), `409 tenant_limit` (the person already created 5
tenants; tenants they only joined do not count).

`POST /api/connections/github` connects the caller's personal GitHub account after they install the GitHub App.
Body `{"code": "<oauth code>", "installationId": <number>}` (`application/json`), both from the redirect to
`/connect/github`. The server exchanges `code` for a user token (never stored or logged), checks that the token's
GitHub user is the caller and that the installation is in that user's installations, reads the account from the
App's own installation record, then inserts (or, for the same tenant's active account, updates) the
`github_connections` row on the `workboard_sync` role in one transaction. Response is the `GithubConnection`.
Errors: `400 invalid_request` (missing code or installationId, not JSON), `400 invalid_code`,
`401 unauthenticated`, `403 admin_only`, `403 not_your_account`, `403 not_your_installation`,
`409 account_connected` (active in another tenant), `422 organization_not_supported`,
`502 github_unavailable` (GitHub call failed), `503 github_app_not_configured`.
Settings `GitHub:App:AppId`, `ClientId`, `ClientSecret`, `PrivateKeyPath`, `Slug`.

`GET /api/issues` query parameters, all optional:

| Name | Meaning |
| --- | --- |
| `projectId` | only issues in repositories of this project |
| `person` | only issues assigned to this GitHub login |
| `assignee` | alias of `person`; if both are given, `person` wins |
| `status` | comma list of `todo`, `in_progress`, `in_review`, `done` (derived status). Any other value, or an empty list → `400 {"error": "invalid_status"}` |
| `stale` | `true` keeps only open issues whose `updatedAt` is 7 or more days old. `false` or absent: no filter |
| `sprint` | only issues in this sprint (uuid) |
| `doneSince` | `week` keeps only issues closed since Monday 00:00 in Asia/Seoul (same boundary as the overview's `doneThisWeek`); open issues are dropped and `doneWithinDays` is ignored. Any other value → `400 {"error": "invalid_doneSince"}` |
| `scope` | `all` includes every closed issue regardless of `doneWithinDays` (use it for progress over all issues). Any other value → `400 {"error": "invalid_scope"}` |
| `doneWithinDays` | closed issues are included only if closed within N days, integer 0..365 (default 7). Ignored when `scope=all`. Out of range → `400 {"error": "invalid_doneWithinDays"}` |

Filters combine with AND. Without `scope=all`, `status=done` therefore returns only issues closed within
`doneWithinDays`. Open issues are always included unless a filter excludes them. Sort: `updatedAt` descending.

`GET /api/meeting-notes` accepts optional `projectId`. Sort: `heldAt` descending.

`GET /api/search` matches `q` (case-insensitive substring, min 2 chars) against issue titles,
comment bodies and meeting note titles/bodies. Up to 20 results per group.

`GET /api/overview` returns the whole overview screen in one response, computed per request inside the
tenant transaction (no cache). Rules:

- Open = `state = 'open'`. Stale = open and `updatedAt` 7 or more days ago. Urgent = `board_fields.priority = 'urgent'`
  and status not `done`; always empty when the tenant has priorities disabled.
- `counts.doneThisWeek` and `people[].doneThisWeek` count issues closed since Monday 00:00 in Asia/Seoul
  (not a rolling 7 days).
- `attention` groups are ordered urgent, reviewWaiting, stale, unassigned. An issue shows only in the first group
  it belongs to; each group is capped at 5, oldest first; `totals` are counted after de-duplication and before the cap.
  `totals.urgentNotStarted` is the number of listed-urgent issues whose status is `todo`.
  `counts.stale` is every stale open issue; `attention.totals.stale` only counts stale issues not already shown
  in an earlier group (urgent, reviewWaiting), so it can be smaller.
  Epics are excluded from `unassigned`.
- `reviewWaiting` lists open PRs with review requested that are linked to an open issue (one entry per issue, the
  longest waiting PR). `waitingSince` is the PR's `github_updated_at`, so it is **approximate**: it moves whenever the PR
  changes, not only when review was requested.
- `people[].overloaded` is true only when the person has 4 or more `in_progress` + `in_review` issues and at least
  2 times the median of that number over all members of the tenant (members with 0 included; even counts average
  the two middle values). People are ordered by open issue count, then login.
- `projects[].byStatus` counts all issues of the project's repositories, closed ones included, with no time window.
  An issue in two projects counts in both.
- `sprint` is the active sprint with the latest start date, or `null` when sprints are disabled or none is active.
  `startDate`/`endDate` are `yyyy-MM-dd`; `daysLeft` counts from today in Asia/Seoul and is never negative.
- `milestones` is always `null` until sync provides milestones.
- `attention.actionItemsWithoutIssue` covers every meeting action item without an issue (no date window);
  `actionItems` covers notes held in the last 14 days whose item has no issue or an unfinished one.
- `activity`: last 3 days, newest first, max 12. A PR event kind is inferred from its current state (merged,
  open with review requested, other open); closed-unmerged PRs are skipped. `actor` is null when the author is not a member.

## Shapes

```ts
type OverviewIssueRef = {
  id: string; repositoryName: string; number: number; title: string;
  status: Status; updatedAt: string;
  assignees: { githubUserId: number; githubLogin: string; displayName: string | null }[];
};

type StatusCounts = { todo: number; inProgress: number; inReview: number; done: number };

type Overview = {
  generatedAt: string;
  counts: { open: number; inProgress: number; inReview: number; stale: number; doneThisWeek: number };
  attention: {
    urgent: { issue: OverviewIssueRef; notStarted: boolean }[];
    reviewWaiting: { issue: OverviewIssueRef; pullRequest: { number: number; title: string;
                     authorGithubLogin: string | null; waitingSince: string } }[];
    stale: { issue: OverviewIssueRef; idleDays: number }[];
    unassigned: OverviewIssueRef[];
    actionItemsWithoutIssue: { id: string; text: string; assignee: Member | null;
                               note: { id: string; title: string; heldAt: string } }[];
    totals: { urgent: number; urgentNotStarted: number; reviewWaiting: number; stale: number; unassigned: number;
              actionItemsWithoutIssue: number };
  };
  people: {
    member: Member; todo: number; inProgress: number; inReview: number; doneThisWeek: number;
    overloaded: boolean;
    current: OverviewIssueRef | null;   // most recently updated in_progress/in_review issue
    lastActivityAt: string | null;      // latest of assigned issue updates and own comments
  }[];
  projects: {
    project: { id: string; name: string; color: string | null; repositories: string[] };
    byStatus: StatusCounts;             // all issues, all time
    openPullRequests: number;
    latestNote: { id: string; title: string; heldAt: string } | null;
  }[];
  sprint: { id: string; name: string; startDate: string; endDate: string; daysLeft: number;
            byStatus: StatusCounts } | null;
  milestones: null;
  actionItems: {
    id: string; text: string; assignee: Member | null;
    issue: { id: string; repositoryName: string; number: number; status: Status } | null;
    note: { id: string; title: string; heldAt: string };
  }[];
  activity: {
    kind: "comment" | "pr_opened" | "pr_review_requested" | "pr_merged" | "issue_closed" | "meeting_note";
    at: string;
    actor: { githubLogin: string | null; displayName: string | null } | null;
    issue: { id: string; repositoryName: string; number: number; title: string } | null;
    pullRequest: { repositoryName: string; number: number; title: string } | null;
    note: { id: string; title: string } | null;
    snippet: string | null;             // first 80 chars of a comment
  }[];
};
```

```ts
type DevUser = {
  githubLogin: string; githubUserId: number; displayName: string | null;
  tenantId: string; tenantName: string;
};

type Member = {
  id: string; githubUserId: number; githubLogin: string;
  displayName: string | null; avatarUrl: string | null; role: "admin" | "member";
};

type Me = {
  member: Member;
  tenant: { id: string; name: string };
  settings: { sprintsEnabled: boolean; sizeEnabled: boolean; priorityEnabled: boolean };
};

type GithubConnection = {
  id: string; accountId: number; accountLogin: string; accountType: "organization" | "user"; installationId: number | null;
  repositorySelection: "all" | "selected" | null; createdAt: string; disconnectedAt: string | null;
};

type Project = { id: string; name: string; color: string | null; repositories: { id: string; name: string }[] };

type Status = "todo" | "in_progress" | "in_review" | "done";
type Category = "todo" | "in_progress" | "done";

type BoardIssue = {
  id: string;
  repository: { id: string; name: string };
  projectIds: string[];
  number: number;
  title: string;
  issueType: string | null;
  state: "open" | "closed";
  status: Status;            // from the issue_status view
  category: Category;        // from the issue_status view
  assignees: { githubUserId: number; githubLogin: string; displayName: string | null }[];
  openPullRequestCount: number;
  commentCount: number;
  parentIssueId: string | null;
  sprint: { id: string; name: string } | null;
  size: "S" | "M" | "L" | null;
  priority: "urgent" | "high" | "normal" | "low" | null;
  updatedAt: string;         // github_updated_at
  closedAt: string | null;
};

type IssueDetail = BoardIssue & {
  body: string | null;
  parent: { id: string; number: number; title: string } | null;
  children: { id: string; repositoryName: string; number: number; title: string; status: Status }[];
  pullRequests: { id: string; number: number; title: string; state: "open" | "closed" | "merged";
                  draft: boolean; reviewRequested: boolean }[];
  comments: { id: string; author: { githubLogin: string | null; displayName: string | null };
              body: string; createdAt: string }[];   // oldest first
  meetingNotes: { id: string; title: string; heldAt: string }[];  // notes with an action item linked to this issue
};

type MeetingNoteSummary = {
  id: string; title: string; heldAt: string;
  project: { id: string; name: string } | null;
  actionItemCount: number; linkedActionItemCount: number;
};

type MeetingNote = {
  id: string; title: string; heldAt: string; bodyMd: string;
  project: { id: string; name: string } | null;
  attendees: Member[];
  actionItems: { id: string; text: string; assignee: Member | null;
                 issue: { id: string; number: number; repositoryName: string; status: Status } | null }[];
};

type SearchResult = {
  issues: { id: string; repositoryName: string; number: number; title: string; status: Status }[];
  comments: { issueId: string; issueTitle: string; snippet: string }[];
  meetingNotes: { id: string; title: string }[];
};
```

Comment authors and assignees are resolved to members by `github_user_id`; unknown users keep
`displayName: null`.
