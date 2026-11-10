# API contract (local skeleton)

Base URL: `http://localhost:5080`. All JSON is camelCase. Times are ISO 8601 UTC strings.

## Identity (local only)

Until GitHub login exists, the caller identifies itself with the `X-Dev-User: <githubLogin>` header.

- The API looks the login up across all tenants (sync role, bypasses RLS), then serves the request
  as that member's tenant.
- Missing or unknown login → `401 {"error": "unauthenticated"}`.
- Only enabled when `ASPNETCORE_ENVIRONMENT=Development`.

## Tenant isolation

Every request that touches tenant data runs inside a transaction on the `workboard_app` role and
first calls `select set_config('app.tenant_id', @tenantId, true)`. RLS policies in
`db/init/001_schema.sql` then hide other tenants' rows. Never query tenant tables on the
`workboard_sync` role from a user request.

## Endpoints

| Method | Path | Auth | Returns |
| --- | --- | --- | --- |
| GET | `/api/health` | none | `{"status":"ok"}` |
| GET | `/api/dev/users` | none (Development only) | `DevUser[]` |
| GET | `/api/me` | dev user | `Me` |
| GET | `/api/members` | dev user | `Member[]` |
| GET | `/api/projects` | dev user | `Project[]` |
| GET | `/api/issues` | dev user | `BoardIssue[]` |
| GET | `/api/issues/{id}` | dev user | `IssueDetail` or 404 |
| GET | `/api/meeting-notes` | dev user | `MeetingNoteSummary[]` |
| GET | `/api/meeting-notes/{id}` | dev user | `MeetingNote` or 404 |
| GET | `/api/search?q=` | dev user | `SearchResult` |

`GET /api/issues` query parameters, all optional:

| Name | Meaning |
| --- | --- |
| `projectId` | only issues in repositories of this project |
| `assignee` | only issues assigned to this GitHub login |
| `doneWithinDays` | closed issues are included only if closed within N days (default 7) |

Open issues are always included. Sort: `updatedAt` descending.

`GET /api/meeting-notes` accepts optional `projectId`. Sort: `heldAt` descending.

`GET /api/search` matches `q` (case-insensitive substring, min 2 chars) against issue titles,
comment bodies and meeting note titles/bodies. Up to 20 results per group.

## Shapes

```ts
type DevUser = { githubLogin: string; displayName: string | null; tenantLogin: string };

type Member = {
  id: string; githubUserId: number; githubLogin: string;
  displayName: string | null; avatarUrl: string | null; role: "admin" | "member";
};

type Me = {
  member: Member;
  tenant: { id: string; githubOrgLogin: string };
  settings: { sprintsEnabled: boolean; sizeEnabled: boolean; priorityEnabled: boolean };
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
