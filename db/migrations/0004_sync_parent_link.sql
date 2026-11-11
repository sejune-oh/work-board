-- The parent of a sub-issue can live in a repository that has not been synced yet. Keeping the parent's
-- GitHub id lets sync link the two as soon as both rows exist.
alter table issues add column parent_github_issue_id bigint;
create index issues_parent_github_issue_id on issues (tenant_id, parent_github_issue_id)
  where parent_github_issue_id is not null;
