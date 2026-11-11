-- Databases created by older init scripts may lack this 0001 column; adopting 0001 as a baseline does not add it.
alter table tenants add column if not exists created_by_github_user_id bigint;
create index if not exists tenants_created_by on tenants (created_by_github_user_id);

-- Renaming a repository recomputes full_name, unless the same update sets full_name to a different value.
create or replace function repositories_fill_full_name() returns trigger
  language plpgsql
as $$
begin
  if new.full_name is null
     or (tg_op = 'UPDATE' and new.name is distinct from old.name and new.full_name is not distinct from old.full_name) then
    select c.account_login || '/' || new.name into new.full_name
      from github_connections c
     where c.id = new.connection_id;
  end if;
  return new;
end $$;

-- Sync upserts labels by GitHub id. Name uniqueness is checked at commit so two labels can swap names.
alter table labels add constraint labels_repository_github_label_key unique (repository_id, github_label_id);
alter table labels
  drop constraint labels_repository_id_name_key,
  add constraint labels_repository_id_name_key unique (repository_id, name) deferrable initially deferred;
