-- Local sample data. Two tenants so tenant isolation can be checked by hand.
-- Runs as the database owner, so RLS does not block these inserts.

insert into tenants (id, github_org_id, github_org_login) values
  ('10000000-0000-0000-0000-000000000001', 1001, 'acme-health'),
  ('10000000-0000-0000-0000-000000000002', 1002, 'globex-labs');

insert into tenant_settings (tenant_id, sprints_enabled, size_enabled, priority_enabled) values
  ('10000000-0000-0000-0000-000000000001', true, true, true),
  ('10000000-0000-0000-0000-000000000002', false, false, false);

insert into members (id, tenant_id, github_user_id, github_login, display_name, role) values
  ('20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', 5001, 'haneul-dev', '김하늘', 'admin'),
  ('20000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000001', 5002, 'minjun-dev', '이민준', 'member'),
  ('20000000-0000-0000-0000-000000000003', '10000000-0000-0000-0000-000000000001', 5003, 'seoyeon-dev', '박서연', 'member'),
  ('20000000-0000-0000-0000-000000000004', '10000000-0000-0000-0000-000000000001', 5004, 'jiho-dev', '최지호', 'member'),
  ('20000000-0000-0000-0000-000000000005', '10000000-0000-0000-0000-000000000001', 5005, 'yuna-dev', '정유나', 'member'),
  ('20000000-0000-0000-0000-000000000006', '10000000-0000-0000-0000-000000000001', 5100, 'shared-dev', '한결 (겸직)', 'member'),
  ('20000000-0000-0000-0000-000000000007', '10000000-0000-0000-0000-000000000002', 5100, 'shared-dev', '한결 (겸직)', 'member'),
  ('20000000-0000-0000-0000-000000000099', '10000000-0000-0000-0000-000000000002', 5099, 'other-org-dev', '다른회사 개발자', 'admin');

insert into repositories (id, tenant_id, github_repo_id, name) values
  ('30000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', 7001, 'booking-api'),
  ('30000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000001', 7002, 'booking-web'),
  ('30000000-0000-0000-0000-000000000003', '10000000-0000-0000-0000-000000000001', 7003, 'cms-api'),
  ('30000000-0000-0000-0000-000000000004', '10000000-0000-0000-0000-000000000001', 7004, 'inbox-web'),
  ('30000000-0000-0000-0000-000000000005', '10000000-0000-0000-0000-000000000001', 7005, 'search-api'),
  ('30000000-0000-0000-0000-000000000099', '10000000-0000-0000-0000-000000000002', 7099, 'secret-repo');

insert into projects (id, tenant_id, name, color) values
  ('40000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', 'Booking', '#2563eb'),
  ('40000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000001', 'Content', '#16a34a'),
  ('40000000-0000-0000-0000-000000000003', '10000000-0000-0000-0000-000000000001', 'Search', '#9333ea');

insert into project_repositories (tenant_id, project_id, repository_id) values
  ('10000000-0000-0000-0000-000000000001', '40000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000001'),
  ('10000000-0000-0000-0000-000000000001', '40000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000002'),
  ('10000000-0000-0000-0000-000000000001', '40000000-0000-0000-0000-000000000002', '30000000-0000-0000-0000-000000000003'),
  ('10000000-0000-0000-0000-000000000001', '40000000-0000-0000-0000-000000000002', '30000000-0000-0000-0000-000000000004'),
  ('10000000-0000-0000-0000-000000000001', '40000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000005');

-- Issues: (id suffix, repo suffix, number, title, type, state, parent suffix, days since update)
insert into issues (id, tenant_id, repository_id, github_issue_id, number, title, issue_type, state,
                    parent_issue_id, author_github_id, github_created_at, closed_at, github_updated_at)
select ('50000000-0000-0000-0000-0000000000' || lpad(v.n::text, 2, '0'))::uuid,
       '10000000-0000-0000-0000-000000000001',
       ('30000000-0000-0000-0000-00000000000' || v.repo)::uuid,
       90000 + v.n, v.num, v.title, v.kind, v.state,
       case when v.parent is null then null
            else ('50000000-0000-0000-0000-0000000000' || lpad(v.parent::text, 2, '0'))::uuid end,
       5001,
       now() - (v.age + 10) * interval '1 day',
       case when v.state = 'closed' then now() - v.age * interval '1 day' end,
       now() - v.age * interval '1 day'
from (values
  ( 1, 1, 101, '환자 예약 API 개편',              'Epic',    'open',   null, 1),
  ( 2, 1, 102, '예약 조회 API 페이지네이션',        'Task',    'open',   1,    0),
  ( 3, 1, 103, '예약 변경 시 알림 발송',            'Feature', 'open',   1,    2),
  ( 4, 2, 201, '예약 화면 달력 컴포넌트',           'Feature', 'open',   1,    1),
  ( 5, 2, 202, '모바일에서 진료 기록 스크롤 깨짐',   'Bug',     'open',   null, 9),
  ( 6, 3, 301, '게시글 예약 발행',                 'Feature', 'open',   null, 0),
  ( 7, 3, 302, '이미지 업로드 용량 제한 안내',       'Task',    'closed', null, 3),
  ( 8, 4, 401, '받은편지함 읽음 표시 동기화',        'Bug',     'open',   null, 1),
  ( 9, 4, 402, '메시지 검색 속도 개선',             'Task',    'open',   null, 12),
  (10, 5, 501, '문서 임베딩 재생성 배치',           'Task',    'open',   null, 2),
  (11, 5, 502, '검색 결과 출처 표시',               'Feature', 'open',   null, 0),
  (12, 5, 503, '토큰 사용량 대시보드',              'Feature', 'open',   null, 4),
  (13, 1, 104, '예약 API 응답 시간 측정',           'Task',    'closed', 1,    5),
  (14, 3, 303, '다국어 슬러그 중복 오류',           'Bug',     'open',   null, 0)
) as v(n, repo, num, title, kind, state, parent, age);

insert into issues (id, tenant_id, repository_id, github_issue_id, number, title, issue_type, state,
                    author_github_id, github_created_at, github_updated_at) values
  ('50000000-0000-0000-0000-000000000099', '10000000-0000-0000-0000-000000000002',
   '30000000-0000-0000-0000-000000000099', 99999, 1, '다른 회사 이슈 (보이면 안 됨)', 'Task', 'open',
   5099, now(), now());

-- Assignees: (issue suffix, github_user_id)
insert into issue_assignees (tenant_id, issue_id, github_user_id)
select '10000000-0000-0000-0000-000000000001',
       ('50000000-0000-0000-0000-0000000000' || lpad(v.n::text, 2, '0'))::uuid, v.uid
from (values (1, 5001), (2, 5002), (3, 5002), (4, 5003), (5, 5003), (6, 5004), (7, 5004),
             (8, 5005), (9, 5005), (10, 5001), (11, 5001), (12, 5002), (13, 5002), (14, 5004)) as v(n, uid);

insert into issue_assignees (tenant_id, issue_id, github_user_id) values
  ('10000000-0000-0000-0000-000000000002', '50000000-0000-0000-0000-000000000099', 5099);

-- Pull requests: (id suffix, repo suffix, number, title, state, review_requested)
insert into pull_requests (id, tenant_id, repository_id, github_pr_id, number, title, state,
                           review_requested, author_github_id, github_updated_at)
select ('60000000-0000-0000-0000-0000000000' || lpad(v.n::text, 2, '0'))::uuid,
       '10000000-0000-0000-0000-000000000001',
       ('30000000-0000-0000-0000-00000000000' || v.repo)::uuid,
       80000 + v.n, v.num, v.title, v.state, v.review, v.author, now() - interval '1 day'
from (values
  (1, 1, 110, '예약 조회 페이지네이션 추가',   'open',   true,  5002),
  (2, 1, 111, '예약 변경 알림 초안',          'open',   false, 5002),
  (3, 2, 210, '달력 컴포넌트',               'open',   true,  5003),
  (4, 3, 310, '예약 발행 스케줄러',           'open',   false, 5004),
  (5, 3, 311, '용량 제한 안내 문구',          'merged', false, 5004),
  (6, 5, 510, '검색 결과 출처 링크',          'open',   true,  5001)
) as v(n, repo, num, title, state, review, author);

insert into issue_pull_requests (tenant_id, issue_id, pull_request_id)
select '10000000-0000-0000-0000-000000000001',
       ('50000000-0000-0000-0000-0000000000' || lpad(v.issue::text, 2, '0'))::uuid,
       ('60000000-0000-0000-0000-0000000000' || lpad(v.pr::text, 2, '0'))::uuid
from (values (2, 1), (3, 2), (4, 3), (6, 4), (7, 5), (11, 6)) as v(issue, pr);

-- One manual override: work started without a PR yet.
insert into sprints (id, tenant_id, name, start_date, end_date, state) values
  ('70000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001',
   '11월 1주기', current_date - 3, current_date + 11, 'active');

insert into board_fields (issue_id, tenant_id, sprint_id, size, priority, status_override) values
  ('50000000-0000-0000-0000-000000000008', '10000000-0000-0000-0000-000000000001',
   '70000000-0000-0000-0000-000000000001', 'M', 'urgent', 'in_progress'),
  ('50000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000001',
   '70000000-0000-0000-0000-000000000001', 'S', 'high', null),
  ('50000000-0000-0000-0000-000000000006', '10000000-0000-0000-0000-000000000001',
   '70000000-0000-0000-0000-000000000001', 'L', 'normal', null),
  ('50000000-0000-0000-0000-000000000014', '10000000-0000-0000-0000-000000000001',
   null, 'S', 'urgent', null);

insert into comments (tenant_id, issue_id, github_comment_id, author_github_id, body,
                      github_created_at, github_updated_at) values
  ('10000000-0000-0000-0000-000000000001', '50000000-0000-0000-0000-000000000002', 40001, 5001,
   '페이지 크기는 기본 20, 최대 100으로 갑시다.', now() - interval '20 hours', now() - interval '20 hours'),
  ('10000000-0000-0000-0000-000000000001', '50000000-0000-0000-0000-000000000002', 40002, 5002,
   '반영했습니다. 리뷰 부탁드려요.', now() - interval '3 hours', now() - interval '3 hours'),
  ('10000000-0000-0000-0000-000000000001', '50000000-0000-0000-0000-000000000005', 40003, 5003,
   'iOS Safari에서만 재현됩니다.', now() - interval '9 days', now() - interval '9 days');

insert into meeting_notes (id, tenant_id, project_id, title, held_at, body_md, created_by) values
  ('80000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001',
   '40000000-0000-0000-0000-000000000001', 'Booking 주간 회의', now() - interval '2 days',
   E'## 안건\n- 예약 API 개편 범위\n\n## 결정\n- 페이지네이션 먼저 배포\n\n## 할 일\n- 알림 발송 설계',
   '20000000-0000-0000-0000-000000000001');

insert into meeting_attendees (tenant_id, note_id, member_id) values
  ('10000000-0000-0000-0000-000000000001', '80000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000001'),
  ('10000000-0000-0000-0000-000000000001', '80000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000002'),
  ('10000000-0000-0000-0000-000000000001', '80000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000003');

insert into meeting_action_items (tenant_id, note_id, text, assignee_member_id, issue_id) values
  ('10000000-0000-0000-0000-000000000001', '80000000-0000-0000-0000-000000000001',
   '예약 변경 알림 발송 설계', '20000000-0000-0000-0000-000000000002', '50000000-0000-0000-0000-000000000003'),
  ('10000000-0000-0000-0000-000000000001', '80000000-0000-0000-0000-000000000001',
   '달력 컴포넌트 접근성 점검', '20000000-0000-0000-0000-000000000003', null);
