-- Derived status, first matching rule wins:
--   1 closed as not planned            -> cancelled
--   2 closed                           -> done
--   3 board_fields.status_override     -> that value
--   4 linked PR merged, issue still open -> in_review
--   5 linked PR open, not draft        -> in_review
--   6 linked PR open, draft            -> in_progress
--   7 otherwise                        -> todo
-- cancelled has category done but is excluded from progress counts by the API.
-- create or replace keeps the existing grants and security_invoker setting.
create or replace view issue_status with (security_invoker = true) as
select s.issue_id,
       s.tenant_id,
       s.status,
       case s.status when 'todo' then 'todo' when 'done' then 'done' when 'cancelled' then 'done' else 'in_progress' end as category
from (
  select i.id as issue_id,
         i.tenant_id,
         case
           when i.state = 'closed' and i.state_reason = 'not_planned' then 'cancelled'
           when i.state = 'closed' then 'done'
           when bf.status_override is not null then bf.status_override
           when exists (select 1 from issue_pull_requests ipr
                          join pull_requests p on p.id = ipr.pull_request_id
                         where ipr.issue_id = i.id and p.state = 'merged') then 'in_review'
           when exists (select 1 from issue_pull_requests ipr
                          join pull_requests p on p.id = ipr.pull_request_id
                         where ipr.issue_id = i.id and p.state = 'open' and not p.draft) then 'in_review'
           when exists (select 1 from issue_pull_requests ipr
                          join pull_requests p on p.id = ipr.pull_request_id
                         where ipr.issue_id = i.id and p.state = 'open' and p.draft) then 'in_progress'
           else 'todo'
         end as status
    from issues i
    left join board_fields bf on bf.issue_id = i.id
) s;
grant select on issue_status to workboard_app;
