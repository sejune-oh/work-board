// Mirrors docs/api-contract.md. Keep the two in sync.

/** One entry per membership: the same person appears once per tenant. */
export type DevUser = {
  githubLogin: string;
  githubUserId: number;
  displayName: string | null;
  tenantId: string;
  tenantLogin: string;
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
  tenant: { id: string; githubOrgLogin: string };
  settings: { sprintsEnabled: boolean; sizeEnabled: boolean; priorityEnabled: boolean };
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

export type SearchResult = {
  issues: { id: string; repositoryName: string; number: number; title: string; status: Status }[];
  comments: { issueId: string; issueTitle: string; snippet: string }[];
  meetingNotes: { id: string; title: string }[];
};
