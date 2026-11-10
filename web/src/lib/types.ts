// Mirrors docs/api-contract.md. Keep the two in sync.

/** One entry per membership: the same person appears once per tenant. */
export type DevUser = {
  githubLogin: string;
  githubUserId: number;
  displayName: string | null;
  tenantId: string;
  tenantName: string;
};

export type Member = {
  id: string;
  githubUserId: number;
  githubLogin: string;
  displayName: string | null;
  avatarUrl: string | null;
  role: "admin" | "member";
};

export type Me = {
  member: Member;
  tenant: { id: string; name: string };
  settings: { sprintsEnabled: boolean; sizeEnabled: boolean; priorityEnabled: boolean };
};

/** POST /api/tenants body. */
export type CreateTenantRequest = { name: string };

/** POST /api/tenants 201 response. Errors: 401 unauthenticated, 400 invalid_name, 409 tenant_limit. */
export type CreateTenantResult = {
  tenant: { id: string; name: string };
  member: Member;
};

export type GithubConnection = {
  id: string;
  accountId: number;
  accountLogin: string;
  accountType: "organization" | "user";
  repositorySelection: "all" | "selected" | null;
  createdAt: string;
  disconnectedAt: string | null;
};

export type Project = {
  id: string;
  name: string;
  color: string | null;
  repositories: { id: string; name: string }[];
};

export type Status = "todo" | "in_progress" | "in_review" | "done";
export type Category = "todo" | "in_progress" | "done";
export type Size = "S" | "M" | "L";
export type Priority = "urgent" | "high" | "normal" | "low";

export type Assignee = { githubUserId: number; githubLogin: string; displayName: string | null };

export type BoardIssue = {
  id: string;
  repository: { id: string; name: string };
  projectIds: string[];
  number: number;
  title: string;
  issueType: string | null;
  state: "open" | "closed";
  status: Status;
  category: Category;
  assignees: Assignee[];
  openPullRequestCount: number;
  commentCount: number;
  parentIssueId: string | null;
  sprint: { id: string; name: string } | null;
  size: Size | null;
  priority: Priority | null;
  updatedAt: string;
  closedAt: string | null;
};

export type IssueDetail = BoardIssue & {
  body: string | null;
  parent: { id: string; number: number; title: string } | null;
  children: { id: string; repositoryName: string; number: number; title: string; status: Status }[];
  pullRequests: {
    id: string;
    number: number;
    title: string;
    state: "open" | "closed" | "merged";
    draft: boolean;
    reviewRequested: boolean;
  }[];
  comments: {
    id: string;
    author: { githubLogin: string | null; displayName: string | null };
    body: string;
    createdAt: string;
  }[];
  meetingNotes: { id: string; title: string; heldAt: string }[];
};

export type MeetingNoteSummary = {
  id: string;
  title: string;
  heldAt: string;
  project: { id: string; name: string } | null;
  actionItemCount: number;
  linkedActionItemCount: number;
};

export type MeetingNote = {
  id: string;
  title: string;
  heldAt: string;
  bodyMd: string;
  project: { id: string; name: string } | null;
  attendees: Member[];
  actionItems: {
    id: string;
    text: string;
    assignee: Member | null;
    issue: { id: string; number: number; repositoryName: string; status: Status } | null;
  }[];
};

export type OverviewIssueRef = {
  id: string;
  repositoryName: string;
  number: number;
  title: string;
  status: Status;
  updatedAt: string;
  assignees: Assignee[];
};

export type StatusCounts = { todo: number; inProgress: number; inReview: number; done: number };

export type OverviewNoteRef = { id: string; title: string; heldAt: string };

export type ActivityKind = "comment" | "pr_opened" | "pr_review_requested" | "pr_merged" | "issue_closed" | "meeting_note";

/** `GET /api/overview`. doneThisWeek counts from Monday 00:00 Asia/Seoul; sprint dates are yyyy-MM-dd. */
export type Overview = {
  generatedAt: string;
  counts: { open: number; inProgress: number; inReview: number; stale: number; doneThisWeek: number };
  attention: {
    urgent: { issue: OverviewIssueRef; notStarted: boolean }[];
    reviewWaiting: {
      issue: OverviewIssueRef;
      pullRequest: { number: number; title: string; authorGithubLogin: string | null; waitingSince: string };
    }[];
    stale: { issue: OverviewIssueRef; idleDays: number }[];
    unassigned: OverviewIssueRef[];
    actionItemsWithoutIssue: { id: string; text: string; assignee: Member | null; note: OverviewNoteRef }[];
    /** De-duplicated counts before the 5-per-group cut; `stale` excludes issues already in an earlier group. */
    totals: {
      urgent: number;
      urgentNotStarted: number;
      reviewWaiting: number;
      stale: number;
      unassigned: number;
      actionItemsWithoutIssue: number;
    };
  };
  people: {
    member: Member;
    todo: number;
    inProgress: number;
    inReview: number;
    doneThisWeek: number;
    overloaded: boolean;
    current: OverviewIssueRef | null;
    lastActivityAt: string | null;
  }[];
  projects: {
    project: { id: string; name: string; color: string | null; repositories: string[] };
    byStatus: StatusCounts;
    openPullRequests: number;
    latestNote: OverviewNoteRef | null;
  }[];
  sprint: { id: string; name: string; startDate: string; endDate: string; daysLeft: number; byStatus: StatusCounts } | null;
  milestones: null;
  actionItems: {
    id: string;
    text: string;
    assignee: Member | null;
    issue: { id: string; repositoryName: string; number: number; status: Status } | null;
    note: OverviewNoteRef;
  }[];
  activity: {
    kind: ActivityKind;
    at: string;
    actor: { githubLogin: string | null; displayName: string | null } | null;
    issue: { id: string; repositoryName: string; number: number; title: string } | null;
    pullRequest: { repositoryName: string; number: number; title: string } | null;
    note: { id: string; title: string } | null;
    snippet: string | null;
  }[];
};

export type SearchResult = {
  issues: { id: string; repositoryName: string; number: number; title: string; status: Status }[];
  comments: { issueId: string; issueTitle: string; snippet: string }[];
  meetingNotes: { id: string; title: string }[];
};
