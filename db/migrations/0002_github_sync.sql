-- Storage for GitHub sync: sync state, extra mirror columns, labels, milestones, cached GitHub users, run log.

-- Repositories ----------------------------------------------------------------
alter table repositories
  add column full_name      text,
  add column html_url       text,
  add column sync_enabled   boolean not null default true,
  add column last_synced_at timestamptz,
  add column sync_cursor    jsonb;

update repositories r
   set full_name = c.account_login || '/' || r.name
  from github_connections c
 where c.tenant_id = r.tenant_id and c.id = r.connection_id;

alter table repositories alter column full_name set not null;
alter table repositories add constraint repositories_connection_full_name_key unique (connection_id, full_name);

-- Rows written without a full name (name only) get owner/name from their connection.
create function repositories_fill_full_name() returns trigger
  language plpgsql
as $$
begin
  if new.full_name is null then
    select c.account_login || '/' || new.name into new.full_name
      from github_connections c
     where c.id = new.connection_id;
  end if;
  return new;
end $$;

create trigger repositories_fill_full_name before insert or update of name, full_name on repositories
  for each row execute function repositories_fill_full_name();

-- Milestones ------------------------------------------------------------------
create table milestones (
  id                  uuid primary key default gen_random_uuid(),
  tenant_id           uuid not null references tenants(id) on delete cascade,
  repository_id       uuid not null,
  github_milestone_id bigint not null,
  number              int    not null,
  title               text   not null,
  state               text   not null check (state in ('open', 'closed')),
  due_on              timestamptz,
  closed_at           timestamptz,
  html_url            text,
  unique (tenant_id, id),
  unique (tenant_id, github_milestone_id),
  unique (repository_id, number),
  foreign key (tenant_id, repository_id) references repositories (tenant_id, id) on delete cascade
);

-- Issues and pull requests ----------------------------------------------------
alter table issues
  add column html_url        text,
  add column github_node_id  text,
  add column state_reason    text check (state_reason in ('completed', 'not_planned', 'reopened', 'duplicate')),
  add column milestone_id    uuid,
  add foreign key (tenant_id, milestone_id) references milestones (tenant_id, id) on delete set null (milestone_id);

alter table pull_requests
  add column html_url       text,
  add column github_node_id text,
  add column merged_at      timestamptz;

create index on issues (milestone_id);

-- Labels ----------------------------------------------------------------------
create table labels (
  id              uuid primary key default gen_random_uuid(),
  tenant_id       uuid not null references tenants(id) on delete cascade,
  repository_id   uuid not null,
  github_label_id bigint,
  name            text not null,
  color           text,
  description     text,
  unique (tenant_id, id),
  unique (repository_id, name),
  foreign key (tenant_id, repository_id) references repositories (tenant_id, id) on delete cascade
);

create table issue_labels (
  tenant_id uuid not null references tenants(id) on delete cascade,
  issue_id  uuid not null,
  label_id  uuid not null,
  primary key (issue_id, label_id),
  foreign key (tenant_id, issue_id) references issues (tenant_id, id) on delete cascade,
  foreign key (tenant_id, label_id) references labels (tenant_id, id) on delete cascade
);
create index on issue_labels (label_id);

-- GitHub users ----------------------------------------------------------------
-- Display data for assignees and authors who are not members. issues.author_github_id,
-- issue_assignees.github_user_id and pull_requests.author_github_id join on (tenant_id, github_user_id);
-- there is no foreign key because those ids may arrive before the user is cached.
create table github_users (
  tenant_id      uuid   not null references tenants(id) on delete cascade,
  github_user_id bigint not null,
  login          text   not null,
  name           text,
  avatar_url     text,
  updated_at     timestamptz not null default now(),
  primary key (tenant_id, github_user_id)
);
create index on github_users (tenant_id, login);

-- Sync runs -------------------------------------------------------------------
create table sync_runs (
  id                   uuid primary key default gen_random_uuid(),
  tenant_id            uuid not null references tenants(id) on delete cascade,
  connection_id        uuid not null,
  repository_id        uuid,
  started_at           timestamptz not null default now(),
  finished_at          timestamptz,
  status               text not null default 'running' check (status in ('running', 'ok', 'failed')),
  issues_synced        int not null default 0,
  pull_requests_synced int not null default 0,
  comments_synced      int not null default 0,
  rate_limit_remaining int,
  error                text,
  foreign key (tenant_id, connection_id) references github_connections (tenant_id, id) on delete cascade,
  foreign key (tenant_id, repository_id) references repositories (tenant_id, id) on delete set null (repository_id)
);
create index on sync_runs (tenant_id, started_at desc);

-- Row level security and grants -----------------------------------------------
do $$
declare t text;
begin
  foreach t in array array['milestones', 'labels', 'issue_labels', 'github_users', 'sync_runs'] loop
    execute format('alter table %I enable row level security', t);
    execute format('alter table %I force row level security', t);
    execute format(
      'create policy tenant_isolation on %I using (tenant_id = app_tenant_id()) with check (tenant_id = app_tenant_id())', t);
  end loop;
end $$;

grant select, insert, update, delete on milestones, labels, issue_labels, github_users, sync_runs to workboard_sync;
grant select on milestones, labels, issue_labels, github_users to workboard_app;
