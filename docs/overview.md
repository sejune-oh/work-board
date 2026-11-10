# 개요 화면 기획

팀 전체의 지금 상황을 한 화면에서 봅니다. 이슈 #24. 디자인은 [design.md](design.md), 시안은
[mockups/overview.html](mockups/overview.html).

## 1. 누가, 언제, 무엇을 정하려고 여는가

| 누가 | 언제 | 정하는 것 |
| --- | --- | --- |
| 팀장, 프로젝트 담당 | 아침, 주간 회의 직전 | 누구를 도울지, 무엇을 먼저 풀지(리뷰, 긴급, 멈춘 일), 일을 옮길지 |
| 팀원 | 출근 직후 | 내가 리뷰해 줘야 할 PR이 있는지, 회의에서 내게 넘어온 일이 있는지 |
| 기획·운영 담당 | 아무 때나 | 프로젝트가 어디까지 왔는지, 회의에서 정한 일이 이슈로 이어졌는지 |

모두 같은 질문으로 모입니다: **"지금 누가 손봐야 할 것이 무엇인가?"** 그래서 화면은 숫자 자랑이 아니라
주의 목록이 중심입니다. 개요는 읽기 전용 허브입니다. 무언가를 고치는 일은 이슈 패널과 보드에서 합니다.

## 2. 위에서 아래로

| 순서 | 구역 | 한 줄 목적 |
| --- | --- | --- |
| 1 | 한 문장 현황 + 핵심 숫자 4개 | 가장 중요한 사실 하나, 그리고 진행 중 / 리뷰 대기 / 멈춤 / 이번 주 완료 |
| 2 | 손봐야 할 일 | 긴급, 리뷰 대기, 7일 넘게 멈춤, 담당자 없음, 이슈로 안 만든 회의 할 일 |
| 3 | 사람 | 사람마다 열린 일의 양과 지금 하는 일 하나 |
| 4 | 프로젝트와 목표 | 프로젝트별 진행률, 이번 주기, (M1 이후) 마일스톤 |
| 5 | 회의에서 나온 할 일 | 회의 결정이 이슈로 이어져 어디까지 왔는지 |
| 6 | 최근 흐름 | 지난 3일 동안의 댓글, PR, 완료, 회의 |

넓은 화면(≥1100px)에서는 2, 3, 6이 넓은 왼쪽 열, 4, 5가 좁은 오른쪽 열로 나란히 놓입니다(읽는 눈이
먼저 닿는 왼쪽 위에 주의 목록). 1은 항상 전체 너비. 휴대폰에서는 위 순서대로 한 줄로 쌓습니다.

## 3. 구역별 규칙

"열린 일" = `issues.state = 'open'`. 상태는 모두 `issue_status` 뷰에서 읽습니다. 멈춤 기준은 `STALE_DAYS = 7`.

### 3-1. 한 문장 현황

아래 규칙을 위에서부터 보고 **처음 맞는 것 하나**를 문장으로 만들고, 둘째로 맞는 것을 이어 붙입니다(최대 두 마디).

| 순위 | 조건 | 문장 조각 |
| --- | --- | --- |
| 1 | 긴급 이슈 중 상태가 할 일 | "긴급 {n}건 중 {k}건은 아직 시작 전입니다" |
| 2 | 리뷰 요청 PR이 있음 | "리뷰를 기다리는 PR이 {n}건 있습니다" |
| 3 | 담당자 없는 열린 일 | "담당자 없는 일이 {n}건 있습니다" |
| 4 | 7일 넘게 멈춘 일 | "{n}건이 7일 넘게 멈춰 있습니다" |
| 5 | 아무것도 해당 없음 | "막힌 곳 없이 {n}건이 진행 중입니다" |

문장은 서버가 만들지 않고 화면이 `attention` 숫자로 조합합니다(문구 수정이 API 변경이 되지 않도록).

핵심 숫자 4개: 진행 중(`in_progress`), 리뷰 대기(`in_review`), 멈춤(열린 일 중 7일 이상), 이번 주 완료
(`closed_at`이 지난 7일 안). 각 숫자는 클릭할 수 있습니다(5장).

빈 상태: 열린 일도 최근 완료도 0건이면 문장 대신 "아직 이슈가 없습니다. 저장소를 연결하면 여기에 모입니다."

### 3-2. 손봐야 할 일

종류별 묶음, 묶음 안은 오래된 순. 묶음마다 최대 5건, 넘으면 "외 n건"(보드로 이동).

| 묶음 | 조건 | 데이터 |
| --- | --- | --- |
| 긴급 | `board_fields.priority = 'urgent'`이고 상태가 완료 아님. 우선순위 기능이 꺼져 있으면 숨김 | `board_fields`, `issue_status` |
| 리뷰 대기 | 리뷰 요청된 열린 PR(`state='open' and review_requested`) | `pull_requests`, `issue_pull_requests` |
| 멈춤 | 열린 이슈, `github_updated_at`이 7일 이상 전 | `issues` |
| 담당자 없음 | 열린 이슈, `issue_assignees` 0건. 큰 목표(`issue_type='Epic'`)는 제외 | `issues`, `issue_assignees` |
| 이슈 없는 회의 할 일 | `meeting_action_items.issue_id is null` | `meeting_action_items`, `meeting_notes` |

한 이슈가 여러 묶음에 걸리면 위쪽 묶음에만 보입니다(중복 없음).
빈 상태: 묶음 전체가 비면 "지금 손봐야 할 일이 없습니다." 묶음 하나만 비면 목록 맨 아래 한 줄에
`muted`로 모아 "담당자 없는 일 없음"처럼 적습니다(비어 있다는 것도 정보).

리뷰 대기 시간은 지금 스키마에 "리뷰 요청 시각"이 없어서 `pull_requests.github_updated_at`으로 대신합니다.
정확한 값은 M1 동기화에서 `review_requested_at`을 받아야 합니다.

### 3-3. 사람

- 팀원 전원(`members`), 열린 일 많은 순. 담당자 없는 일은 여기 넣지 않습니다(3-2에 있음).
- 한 줄: 이름, 부하 막대(진행 중 + 리뷰 중 / 할 일), 숫자, **지금 하는 일 하나**(진행 중·리뷰 중 가운데 가장 최근 활동), 마지막 활동.
- 몰림 도장: design.md의 부하 막대 규칙.
- 빈 상태: 열린 일 0건이면 "맡은 일 없음". 팀원이 0명이면 "팀원이 없습니다. 설정에서 GitHub 조직을 연결하세요."

### 3-4. 프로젝트와 목표

- 프로젝트마다: 색 표식, 이름, 저장소, 상태별 진행률 막대, `완료 n / 전체 m`, 열린 PR 수, 최근 회의 하나.
- 진행률 분모는 **그 프로젝트의 모든 이슈**입니다. `GET /api/issues`는 7일 지난 완료를 빼므로 지금 프로젝트 보드의
  퍼센트는 실제보다 낮게 나옵니다. 개요는 이 값을 서버에서 따로 셉니다(보드도 같은 값을 쓰도록 맞추길 권함).
- 이번 주기(설정에서 켠 경우): `sprints.state='active'` 하나의 이름, 남은 날, 주기에 든 이슈의 상태 막대.
- 마일스톤: **M1 동기화 이후**. 지금 스키마에 마일스톤 테이블이 없습니다. 동기화가 들어오면 열린 마일스톤마다
  마감일, 진행률을 같은 막대로 보여 줍니다. 그전까지 이 칸은 숨깁니다.
- 빈 상태: 프로젝트 0개면 "프로젝트(저장소 묶음)가 없습니다. 설정에서 만드세요."

### 3-5. 회의에서 나온 할 일

- 최근 14일 회의의 할 일 중 **아직 끝나지 않은 것**: 이슈가 없거나, 연결된 이슈가 완료가 아님.
- 한 줄: 할 일 문장, 담당자, 연결된 이슈의 상태(없으면 "이슈 없음" 주의 표시), 출처 회의.
- 빈 상태: "최근 회의에서 남은 할 일이 없습니다."

### 3-6. 최근 흐름

- 지난 3일, 최대 12건, 최신 순. 종류: 댓글(`comments`), PR 열림·리뷰 요청·병합(`pull_requests`), 이슈 완료(`issues.closed_at`), 회의 노트(`meeting_notes`).
- 한 줄: 누가, 무엇을, 어디에서. 예: "이민준 · booking-api #102에 댓글 · 3시간 전".
- PR은 지금 "언제 무엇이 바뀌었는지"를 하나만 알 수 있어서(`github_updated_at`, `state`) 사건 종류를 상태로 추정합니다.
  정확한 사건 기록은 웹훅 처리 이후 이벤트 테이블이 필요합니다.
- 빈 상태: "지난 3일 동안 움직임이 없습니다."

## 4. 홈 화면

**제안: 개요를 `/`로 두고, 내 작업은 `/me`로 옮깁니다.**

- 이 제품의 첫 원칙이 "팀 전체를 한눈에"라서 문을 열자마자 보이는 화면이 그것이어야 합니다.
- 내 작업은 개요 안에서도 잡힙니다(리뷰 대기와 회의 할 일은 이름 옆에 "나" 표시). 깊이 볼 때만 `/me`로 갑니다.
- 왼쪽 메뉴 순서: 개요, 내 작업, 사람별 보드, 프로젝트별 보드, 회의 노트, 설정.
- 정적 내보내기라 서버 리다이렉트가 없습니다. 예전 주소 `/`를 북마크한 사람은 개요를 보게 되고, 그것이 의도입니다.

반대 의견에 대한 답: 개인 위주 팀이라면 `/`를 내 작업으로 두고 개요를 `/overview`에 둘 수 있습니다.
설정에서 고르게 하는 것은 하지 않습니다(화면이 사람마다 달라지면 "같은 화면을 보며 이야기하기"가 깨짐).

## 5. 클릭하면 어디로

| 누르는 것 | 가는 곳 |
| --- | --- |
| 이슈 한 줄, 리뷰 대기 PR | 지금 화면 위에 이슈 패널 `/?issue={issueId}` |
| 핵심 숫자 "진행 중", "리뷰 대기" | `/people?status=in_progress` / `?status=in_review` (*) |
| 핵심 숫자 "멈춤" | `/people?stale=1` (*) |
| 핵심 숫자 "이번 주 완료" | `/people?status=done` (*) |
| 사람 한 줄 | `/people?person={githubLogin}` (*) |
| 사람 줄의 "지금 하는 일" | 이슈 패널 |
| 프로젝트 | `/projects?project={projectId}` (있음) |
| 이번 주기 | `/projects?sprint={sprintId}` (*) |
| 회의 할 일, 최근 흐름의 회의 | `/notes/view?id={noteId}` (있음) |
| "이슈로 만들기" | M2 쓰기 기능 이후. 그전에는 회의 노트로 이동 |

(*) 표시는 보드에 필터 쿼리를 새로 받아야 합니다. 보드가 읽는 쿼리는 `?issue=`, `?project=` 뿐입니다.
필터는 화면에서만 거릅니다(`/api/issues`는 이미 전체를 줌). 새 화면을 만들지 않고 기존 보드에 붙입니다.

## 6. `GET /api/overview`

한 번의 요청으로 개요 전체를 받습니다. 화면에서 여러 API를 엮지 않는 이유: 프로젝트 진행률 분모,
회의 할 일 상태처럼 지금 API로는 정확히 셀 수 없는 값이 있어서입니다.

```ts
type OverviewIssueRef = {
  id: string; repositoryName: string; number: number; title: string;
  status: Status; updatedAt: string;
  assignees: { githubUserId: number; githubLogin: string; displayName: string | null }[];
};

type Overview = {
  generatedAt: string;
  counts: {
    open: number;
    inProgress: number;          // issue_status.status = 'in_progress'
    inReview: number;            // issue_status.status = 'in_review'
    stale: number;               // open and github_updated_at < now() - 7 days
    doneThisWeek: number;        // closed_at >= now() - 7 days
  };
  attention: {
    urgent: (OverviewIssueRef & { notStarted: boolean })[];   // [] when priority is disabled
    reviewWaiting: { issue: OverviewIssueRef; pullRequest: { number: number; title: string;
                     authorGithubLogin: string | null; waitingSince: string } }[];
    stale: (OverviewIssueRef & { idleDays: number })[];
    unassigned: OverviewIssueRef[];
    actionItemsWithoutIssue: { id: string; text: string; assignee: Member | null;
                               note: { id: string; title: string; heldAt: string } }[];
    totals: { urgent: number; reviewWaiting: number; stale: number; unassigned: number;
              actionItemsWithoutIssue: number };  // before the 5-per-group cut
  };
  people: {
    member: Member;
    todo: number; inProgress: number; inReview: number; doneThisWeek: number;
    current: OverviewIssueRef | null;   // most recently updated in_progress/in_review issue
    lastActivityAt: string | null;      // max of assigned issue updates and own comments
  }[];
  projects: {
    project: { id: string; name: string; color: string | null; repositories: string[] };
    byStatus: { todo: number; inProgress: number; inReview: number; done: number };  // all issues, all time
    openPullRequests: number;
    latestNote: { id: string; title: string; heldAt: string } | null;
  }[];
  sprint: {                         // null when sprints are disabled or none is active
    id: string; name: string; startDate: string; endDate: string; daysLeft: number;
    byStatus: { todo: number; inProgress: number; inReview: number; done: number };
  } | null;
  milestones: null;                 // filled after M1 sync; shape decided then
  actionItems: {                    // last 14 days, not finished
    id: string; text: string; assignee: Member | null;
    issue: { id: string; repositoryName: string; number: number; status: Status } | null;
    note: { id: string; title: string; heldAt: string };
  }[];
  activity: {                       // last 3 days, newest first, max 12
    kind: "comment" | "pr_opened" | "pr_review_requested" | "pr_merged" | "issue_closed" | "meeting_note";
    at: string;
    actor: { githubLogin: string | null; displayName: string | null } | null;
    issue: { id: string; repositoryName: string; number: number; title: string } | null;
    pullRequest: { repositoryName: string; number: number; title: string } | null;
    note: { id: string; title: string } | null;
    snippet: string | null;         // first 80 chars of a comment
  }[];
};
```

계산 근거:

| 필드 | 계산 |
| --- | --- |
| `counts.*` | `issues` ⋈ `issue_status` 한 번으로 group by. `stale`은 `state='open' and github_updated_at < now() - interval '7 days'` |
| `attention.urgent` | `board_fields.priority='urgent'` ⋈ `issue_status.status <> 'done'`. `notStarted = status = 'todo'`. `tenant_settings.priority_enabled`가 false면 빈 배열 |
| `attention.reviewWaiting` | `pull_requests where state='open' and review_requested` ⋈ `issue_pull_requests`. 이슈 없는 PR은 제외. `waitingSince = github_updated_at` |
| `attention.unassigned` | `issues i where state='open' and coalesce(issue_type,'') <> 'Epic' and not exists (issue_assignees)` |
| `attention.actionItemsWithoutIssue` | `meeting_action_items where issue_id is null` ⋈ `meeting_notes` |
| `people` | `members` ⟕ `issue_assignees`(github_user_id) ⋈ `issue_status`. `lastActivityAt`은 담당 이슈 `github_updated_at`과 `comments.author_github_id` 중 최댓값 |
| `projects.byStatus` | `project_repositories` ⋈ `issues` ⋈ `issue_status`, **완료 기간 제한 없음** |
| `projects.openPullRequests` | `pull_requests where state='open'` ⋈ `project_repositories` |
| `sprint` | `sprints where state='active'` 하나 ⋈ `board_fields.sprint_id` ⋈ `issue_status`. `sprints_enabled`가 false면 null |
| `actionItems` | `meeting_action_items` ⋈ `meeting_notes(held_at >= now() - 14 days)` ⟕ `issue_status`, 상태가 완료인 것 제외 |
| `activity` | `comments`(github_created_at), `pull_requests`(github_updated_at, state로 종류 추정), `issues.closed_at`, `meeting_notes.held_at`을 union all 후 정렬 |

구현 메모:

- 모든 쿼리는 다른 API와 같이 `workboard_app` 역할 트랜잭션 안에서 `app.tenant_id`를 건 뒤 실행합니다. 새 권한은 필요 없습니다.
- 10명 규모에서는 쿼리 여러 개를 한 트랜잭션에서 차례로 실행해도 충분합니다. 캐시는 두지 않습니다.
- 계약은 `docs/api-contract.md`와 `web/src/lib/types.ts`에 함께 추가합니다.
- M1 동기화가 필요한 것: `milestones`, 정확한 `waitingSince`(리뷰 요청 시각), PR 사건 기록(`activity`의 PR 종류 정확도).

## 7. 정할 것

1. 개요를 `/`로 둘지(제안), `/overview`로 둘지.
2. 보드 필터 쿼리(`status`, `stale`, `person`, `sprint`)를 개요 PR에 같이 넣을지, 따로 이슈로 뺄지.
3. 프로젝트 진행률 분모를 보드에서도 "모든 이슈"로 바꿀지(지금은 최근 7일 완료만 셈).

## 결정 (2026-11-10)

- Overview는 홈 `/`에 둡니다. 내 작업은 `/me`로 옮깁니다.
- 보드 필터 쿼리(`status`, `stale`, `person`, `sprint`)는 Overview 구현과 같은 작업에서 넣습니다.
- 프로젝트 진행률은 최근 7일 완료가 아니라 전체 이슈 기준으로 셉니다. 보드도 같은 기준으로 맞춥니다.
- "취소"는 다섯 번째 열을 만들지 않고 완료 열 아래 접힌 묶음(회색 점, 가운데 줄)으로 보입니다.
- "몰림" 표시는 진행 중 4건 이상이면서 팀 중앙값의 2배 이상일 때만 붙입니다.
