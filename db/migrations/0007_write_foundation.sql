-- Shared base for board writes: who changed what, structured issue history, replay protection.

create table audit_log (
  id          uuid primary key default gen_random_uuid(),
  tenant_id   uuid not null references tenants(id) on delete cascade,
  member_id   uuid,
  action      text not null,
  target_type text not null,
  target_id   uuid,
  before      jsonb,
  after       jsonb,
  created_at  timestamptz not null default now(),
  foreign key (tenant_id, member_id) references members (tenant_id, id) on delete set null (member_id)
);
create index audit_log_tenant_created on audit_log (tenant_id, created_at desc);
create index audit_log_target on audit_log (tenant_id, target_type, target_id);

-- One row per board-field change, with typed columns so reports can aggregate without parsing JSON.
create table issue_events (
  id         uuid primary key default gen_random_uuid(),
  tenant_id  uuid not null references tenants(id) on delete cascade,
  issue_id   uuid not null,
  kind       text not null check (kind in (
               'sprint_added', 'sprint_removed', 'status_set', 'status_cleared',
               'priority_set', 'size_set', 'carried_over')),
  from_value text,
  to_value   text,
  sprint_id  uuid,
  member_id  uuid,
  at         timestamptz not null default now(),
  foreign key (tenant_id, issue_id) references issues (tenant_id, id) on delete cascade,
  foreign key (tenant_id, sprint_id) references sprints (tenant_id, id) on delete set null (sprint_id),
  foreign key (tenant_id, member_id) references members (tenant_id, id) on delete set null (member_id)
);
create index issue_events_sprint_at on issue_events (tenant_id, sprint_id, at);
create index issue_events_issue_at on issue_events (issue_id, at);

-- Stored response of a write that carried an Idempotency-Key header. Rows older than 24 hours are
-- treated as absent and replaced when the same key is used again.
create table idempotency_keys (
  tenant_id    uuid not null references tenants(id) on delete cascade,
  key          text not null,
  member_id    uuid,
  request_hash text not null,
  status_code  int  not null,
  response     jsonb,
  created_at   timestamptz not null default now(),
  primary key (tenant_id, key),
  foreign key (tenant_id, member_id) references members (tenant_id, id) on delete set null (member_id)
);
create index idempotency_keys_created on idempotency_keys (created_at);

do $$
declare t text;
begin
  foreach t in array array['audit_log', 'issue_events', 'idempotency_keys'] loop
    execute format('alter table %I enable row level security', t);
    execute format('alter table %I force row level security', t);
    execute format(
      'create policy tenant_isolation on %I using (tenant_id = app_tenant_id()) with check (tenant_id = app_tenant_id())', t);
  end loop;
end $$;

grant select, insert, update, delete on audit_log, issue_events, idempotency_keys to workboard_sync;
grant select, insert on audit_log, issue_events to workboard_app;
grant select, insert, delete on idempotency_keys to workboard_app;
