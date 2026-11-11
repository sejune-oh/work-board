-- Sprints: authorship, optimistic-concurrency timestamp, unique names, and the record of carry-overs
-- that makes a carry-over undoable.

alter table sprints
  add column created_by uuid,
  add column created_at timestamptz not null default now(),
  add column updated_at timestamptz not null default now(),
  add constraint sprints_dates_ordered check (end_date >= start_date),
  add foreign key (tenant_id, created_by) references members (tenant_id, id) on delete set null (created_by);

create unique index sprints_tenant_name on sprints (tenant_id, lower(name));

-- One row per carry-over: the unfinished issues of from_sprint moved to to_sprint in one transaction.
create table sprint_carry_overs (
  id             uuid primary key default gen_random_uuid(),
  tenant_id      uuid not null references tenants(id) on delete cascade,
  from_sprint_id uuid not null,
  to_sprint_id   uuid not null,
  member_id      uuid,
  created_at     timestamptz not null default now(),
  undone_at      timestamptz,
  undone_by      uuid,
  unique (tenant_id, id),
  check (from_sprint_id <> to_sprint_id),
  foreign key (tenant_id, from_sprint_id) references sprints (tenant_id, id) on delete cascade,
  foreign key (tenant_id, to_sprint_id) references sprints (tenant_id, id) on delete cascade,
  foreign key (tenant_id, member_id) references members (tenant_id, id) on delete set null (member_id),
  foreign key (tenant_id, undone_by) references members (tenant_id, id) on delete set null (undone_by)
);
create index sprint_carry_overs_from on sprint_carry_overs (tenant_id, from_sprint_id, created_at desc);

create table sprint_carry_over_issues (
  tenant_id     uuid not null references tenants(id) on delete cascade,
  carry_over_id uuid not null,
  issue_id      uuid not null,
  primary key (carry_over_id, issue_id),
  foreign key (tenant_id, carry_over_id) references sprint_carry_overs (tenant_id, id) on delete cascade,
  foreign key (tenant_id, issue_id) references issues (tenant_id, id) on delete cascade
);

do $$
declare t text;
begin
  foreach t in array array['sprint_carry_overs', 'sprint_carry_over_issues'] loop
    execute format('alter table %I enable row level security', t);
    execute format('alter table %I force row level security', t);
    execute format(
      'create policy tenant_isolation on %I using (tenant_id = app_tenant_id()) with check (tenant_id = app_tenant_id())', t);
  end loop;
end $$;

grant select, insert, update, delete on sprint_carry_overs, sprint_carry_over_issues to workboard_sync;
grant select, insert, update on sprint_carry_overs to workboard_app;
grant select, insert on sprint_carry_over_issues to workboard_app;
