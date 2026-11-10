-- Work board schema. Every tenant-owned row carries tenant_id and is filtered by RLS
-- against the `app.tenant_id` setting that the API sets per request.

create extension if not exists pgcrypto;

-- Roles -----------------------------------------------------------------------
-- workboard_app  : user-facing API requests, always subject to RLS
-- workboard_sync : GitHub sync jobs that write across tenants, bypasses RLS
do $$
begin
  if not exists (select 1 from pg_roles where rolname = 'workboard_app') then
    create role workboard_app login password 'workboard_app';
  end if;
  if not exists (select 1 from pg_roles where rolname = 'workboard_sync') then
    create role workboard_sync login password 'workboard_sync' bypassrls;
  end if;
end $$;

create or replace function app_tenant_id() returns uuid
  language sql stable
as $$ select nullif(current_setting('app.tenant_id', true), '')::uuid $$;

-- Tenants and people ----------------------------------------------------------
create table tenants (
  id               uuid primary key default gen_random_uuid(),
  github_org_id    bigint not null unique,
  github_org_login text   not null,
  installation_id  bigint,
  created_at       timestamptz not null default now()
);

create table members (
  id             uuid primary key default gen_random_uuid(),
  tenant_id      uuid not null references tenants(id) on delete cascade,
  github_user_id bigint not null,
  github_login   text   not null,
  display_name   text,
  avatar_url     text,
  role           text   not null default 'member' check (role in ('admin', 'member')),
  created_at     timestamptz not null default now(),
  unique (tenant_id, id),
  unique (tenant_id, github_user_id),
  unique (tenant_id, github_login)
);

create table tenant_settings (
  tenant_id        uuid primary key references tenants(id) on delete cascade,
  sprints_enabled  boolean not null default false,
  size_enabled     boolean not null default false,
  priority_enabled boolean not null default false
);

-- GitHub mirror (written by sync only) -----------------------------------------
create table repositories (
  id             uuid primary key default gen_random_uuid(),
  tenant_id      uuid not null references tenants(id) on delete cascade,
  github_repo_id bigint not null,
  name           text   not null,
  archived       boolean not null default false,
  unique (tenant_id, id),
  unique (tenant_id, github_repo_id),
  unique (tenant_id, name)
);

create table issues (
  id                uuid primary key default gen_random_uuid(),
  tenant_id         uuid not null references tenants(id) on delete cascade,
  repository_id     uuid not null,
  github_issue_id   bigint not null,
  number            int    not null,
  title             text   not null,
  body              text,
  issue_type        text,
  state             text   not null check (state in ('open', 'closed')),
  parent_issue_id   uuid,
  author_github_id  bigint,
  github_created_at timestamptz not null,
  closed_at         timestamptz,
  github_updated_at timestamptz not null,
  unique (tenant_id, id),
  unique (tenant_id, github_issue_id),
  unique (repository_id, number),
  foreign key (tenant_id, repository_id) references repositories (tenant_id, id) on delete cascade,
  foreign key (tenant_id, parent_issue_id) references issues (tenant_id, id) on delete set null (parent_issue_id)
);

create table issue_assignees (
  tenant_id      uuid   not null references tenants(id) on delete cascade,
  issue_id       uuid   not null,
  github_user_id bigint not null,
  primary key (issue_id, github_user_id),
  foreign key (tenant_id, issue_id) references issues (tenant_id, id) on delete cascade
);

create table pull_requests (
  id                uuid primary key default gen_random_uuid(),
  tenant_id         uuid not null references tenants(id) on delete cascade,
  repository_id     uuid not null,
  github_pr_id      bigint not null,
  number            int    not null,
  title             text   not null,
  state             text   not null check (state in ('open', 'closed', 'merged')),
  draft             boolean not null default false,
  review_requested  boolean not null default false,
  author_github_id  bigint,
  github_updated_at timestamptz not null,
  unique (tenant_id, id),
  unique (tenant_id, github_pr_id),
  unique (repository_id, number),
  foreign key (tenant_id, repository_id) references repositories (tenant_id, id) on delete cascade
);

create table issue_pull_requests (
  tenant_id       uuid not null references tenants(id) on delete cascade,
  issue_id        uuid not null,
  pull_request_id uuid not null,
  primary key (issue_id, pull_request_id),
  foreign key (tenant_id, issue_id) references issues (tenant_id, id) on delete cascade,
  foreign key (tenant_id, pull_request_id) references pull_requests (tenant_id, id) on delete cascade
);

create table comments (
  id                uuid primary key default gen_random_uuid(),
  tenant_id         uuid not null references tenants(id) on delete cascade,
  issue_id          uuid not null,
  github_comment_id bigint not null,
  author_github_id  bigint,
  body              text   not null,
  github_created_at timestamptz not null,
  github_updated_at timestamptz not null,
  unique (tenant_id, github_comment_id),
  foreign key (tenant_id, issue_id) references issues (tenant_id, id) on delete cascade
);

-- Board-only data -------------------------------------------------------------
create table projects (
  id        uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references tenants(id) on delete cascade,
  name      text not null,
  color     text,
  unique (tenant_id, id),
  unique (tenant_id, name)
);

create table project_repositories (
  tenant_id     uuid not null references tenants(id) on delete cascade,
  project_id    uuid not null,
  repository_id uuid not null,
  primary key (project_id, repository_id),
  foreign key (tenant_id, project_id) references projects (tenant_id, id) on delete cascade,
  foreign key (tenant_id, repository_id) references repositories (tenant_id, id) on delete cascade
);

create table sprints (
  id         uuid primary key default gen_random_uuid(),
  tenant_id  uuid not null references tenants(id) on delete cascade,
  name       text not null,
  start_date date not null,
  end_date   date not null,
  state      text not null default 'planned' check (state in ('planned', 'active', 'closed')),
  unique (tenant_id, id)
);

create table board_fields (
  issue_id        uuid primary key,
  tenant_id       uuid not null references tenants(id) on delete cascade,
  sprint_id       uuid,
  size            text check (size in ('S', 'M', 'L')),
  priority        text check (priority in ('urgent', 'high', 'normal', 'low')),
  status_override text check (status_override in ('todo', 'in_progress', 'in_review', 'done')),
  updated_at      timestamptz not null default now(),
  foreign key (tenant_id, issue_id) references issues (tenant_id, id) on delete cascade,
  foreign key (tenant_id, sprint_id) references sprints (tenant_id, id) on delete set null (sprint_id)
);

create table meeting_notes (
  id         uuid primary key default gen_random_uuid(),
  tenant_id  uuid not null references tenants(id) on delete cascade,
  project_id uuid,
  title      text not null,
  held_at    timestamptz not null,
  body_md    text not null default '',
  created_by uuid,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (tenant_id, id),
  foreign key (tenant_id, project_id) references projects (tenant_id, id) on delete set null (project_id),
  foreign key (tenant_id, created_by) references members (tenant_id, id) on delete set null (created_by)
);

create table meeting_attendees (
  tenant_id uuid not null references tenants(id) on delete cascade,
  note_id   uuid not null,
  member_id uuid not null,
  primary key (note_id, member_id),
  foreign key (tenant_id, note_id) references meeting_notes (tenant_id, id) on delete cascade,
  foreign key (tenant_id, member_id) references members (tenant_id, id) on delete cascade
);

create table meeting_action_items (
  id                 uuid primary key default gen_random_uuid(),
  tenant_id          uuid not null references tenants(id) on delete cascade,
  note_id            uuid not null,
  text               text not null,
  assignee_member_id uuid,
  issue_id           uuid,
  created_at         timestamptz not null default now(),
  foreign key (tenant_id, note_id) references meeting_notes (tenant_id, id) on delete cascade,
  foreign key (tenant_id, assignee_member_id) references members (tenant_id, id) on delete set null (assignee_member_id),
  foreign key (tenant_id, issue_id) references issues (tenant_id, id) on delete set null (issue_id)
);

-- Operations ------------------------------------------------------------------
create table webhook_deliveries (
  delivery_id  text primary key,
  tenant_id    uuid references tenants(id) on delete cascade,
  event        text not null,
  received_at  timestamptz not null default now(),
  processed_at timestamptz
);

create table ai_usage (
  id            bigserial primary key,
  tenant_id     uuid not null references tenants(id) on delete cascade,
  feature       text not null,
  input_tokens  int  not null default 0,
  output_tokens int  not null default 0,
  created_at    timestamptz not null default now()
);

create index on issues (tenant_id, state);
create index on issues (parent_issue_id);
create index on issue_assignees (tenant_id, github_user_id);
create index on pull_requests (tenant_id, state);
create index on comments (issue_id, github_created_at);
create index on meeting_action_items (issue_id);

-- Derived status --------------------------------------------------------------
-- First matching rule wins: closed -> override -> open PR with review -> open PR -> todo.
-- category groups statuses the way Jira status categories do: todo / in_progress / done.
create view issue_status with (security_invoker = true) as
select s.issue_id,
       s.tenant_id,
       s.status,
       case s.status when 'todo' then 'todo' when 'done' then 'done' else 'in_progress' end as category
from (
  select i.id as issue_id,
         i.tenant_id,
         case
           when i.state = 'closed' then 'done'
           when bf.status_override is not null then bf.status_override
           when exists (select 1 from issue_pull_requests ipr
                          join pull_requests p on p.id = ipr.pull_request_id
                         where ipr.issue_id = i.id and p.state = 'open' and p.review_requested) then 'in_review'
           when exists (select 1 from issue_pull_requests ipr
                          join pull_requests p on p.id = ipr.pull_request_id
                         where ipr.issue_id = i.id and p.state = 'open') then 'in_progress'
           else 'todo'
         end as status
    from issues i
    left join board_fields bf on bf.issue_id = i.id
) s;

-- Row level security ----------------------------------------------------------
alter table tenants enable row level security;
alter table tenants force row level security;
create policy tenant_isolation on tenants using (id = app_tenant_id());

do $$
declare t text;
begin
  foreach t in array array[
    'members', 'tenant_settings', 'repositories', 'issues', 'issue_assignees', 'pull_requests',
    'issue_pull_requests', 'comments', 'projects', 'project_repositories', 'sprints', 'board_fields',
    'meeting_notes', 'meeting_attendees', 'meeting_action_items', 'ai_usage'
  ] loop
    execute format('alter table %I enable row level security', t);
    execute format('alter table %I force row level security', t);
    execute format(
      'create policy tenant_isolation on %I using (tenant_id = app_tenant_id()) with check (tenant_id = app_tenant_id())', t);
  end loop;
end $$;

-- Grants ----------------------------------------------------------------------
-- The API role reads everything it needs and writes only board-only tables. GitHub mirror data,
-- tenants and members are written by sync (workboard_sync) alone.
grant usage on schema public to workboard_app, workboard_sync;
grant select, insert, update, delete on all tables in schema public to workboard_sync;
grant usage, select on all sequences in schema public to workboard_app, workboard_sync;

grant select on tenants, members, repositories, issues, issue_assignees, pull_requests,
  issue_pull_requests, comments, issue_status to workboard_app;
grant select, insert, update, delete on tenant_settings, projects, project_repositories, sprints,
  board_fields, meeting_notes, meeting_attendees, meeting_action_items to workboard_app;
