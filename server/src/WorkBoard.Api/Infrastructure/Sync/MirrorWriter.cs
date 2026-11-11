using Dapper;
using Npgsql;
using WorkBoard.Api.Infrastructure.Data;
using WorkBoard.Api.Infrastructure.GitHub;
using WorkBoard.Api.Infrastructure.Identity;

namespace WorkBoard.Api.Infrastructure.Sync;

public sealed record MirrorResult(Guid RepositoryId, int Issues, int PullRequests, int Comments);

// Applied is false when a newer copy of the issue was already stored and the response was ignored.
public sealed record MirroredIssue(Guid IssueId, bool Applied);

// Writes GitHub data into one tenant's mirror tables on the workboard_sync role (which bypasses RLS),
// so every statement filters on tenant_id itself.
//
// One snapshot is applied in one transaction. Rows are upserted by GitHub id; an incoming issue, pull
// request or comment older than the stored github_updated_at is ignored along with its child rows.
// Milestones and labels arrive as full lists, so ones missing from the list are deleted. Issues, pull
// requests, comments and repositories that disappear from GitHub are kept.
public sealed class MirrorWriter([FromKeyedServices(DataKeys.Sync)] NpgsqlDataSource sync)
{
    public async Task UpsertRepositoriesAsync(
        Guid tenantId, Guid connectionId, IEnumerable<SourceRepository> repositories, CancellationToken ct)
    {
        await using var conn = await sync.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var db = new Db(conn, tx, tenantId, ct);
        await db.SetTenantAsync();
        foreach (var repo in repositories)
            await UpsertRepositoryAsync(db, connectionId, repo);
        await tx.CommitAsync(ct);
    }

    public async Task<MirrorResult> ApplyAsync(
        Guid tenantId, Guid connectionId, RepositorySnapshot snapshot, CancellationToken ct)
    {
        await using var conn = await sync.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var db = new Db(conn, tx, tenantId, ct);
        await db.SetTenantAsync();

        var repoId = await UpsertRepositoryAsync(db, connectionId, snapshot.Repository);
        await UpsertUsersAsync(db, SnapshotUsers(snapshot));
        var milestones = await UpsertMilestonesAsync(db, repoId, snapshot.Milestones);
        var labels = await UpsertLabelsAsync(db, repoId, snapshot.Labels);
        var issues = await UpsertIssuesAsync(db, repoId, snapshot.Issues, milestones);
        await ReplaceIssueChildrenAsync(db, repoId, issues, labels);
        await ResolveParentsAsync(db, issues);
        var pullRequests = await UpsertPullRequestsAsync(db, repoId, snapshot.PullRequests);
        await ReplaceClosingLinksAsync(db, repoId, pullRequests);
        var comments = await UpsertCommentsAsync(db, repoId, snapshot.Comments);

        await tx.CommitAsync(ct);
        return new MirrorResult(repoId, issues.Count, pullRequests.Count, comments);
    }

    // Mirrors the response of a board write right away. Same upsert and "older is ignored" rule as a sync run, on
    // one issue. A REST response does not carry the parent issue, so the stored parent link is left alone.
    // When the stored state differs from the response, a closed or reopened issue_events row is written in the
    // same transaction, attributed to memberId.
    public async Task<MirroredIssue?> ApplyWrittenIssueAsync(
        Guid tenantId, Guid repositoryId, SourceIssue issue, Guid? memberId, CancellationToken ct)
    {
        await using var conn = await sync.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var db = new Db(conn, tx, tenantId, ct);
        await db.SetTenantAsync();

        if (await db.Scalar<Guid?>("select id from repositories where tenant_id = @tenantId and id = @repositoryId",
                new { repositoryId }) is null)
            return null;

        var previous = await db.Scalar<PreviousState>("""
            select state as State, state_reason as StateReason from issues where tenant_id = @tenantId and github_issue_id = @githubId
            """, new { githubId = issue.DatabaseId });

        await UpsertUsersAsync(db, [issue.Author, .. issue.Assignees]);
        var milestones = await db.Query<(int Number, Guid Id)>(
            "select number, id from milestones where tenant_id = @tenantId and repository_id = @repositoryId", new { repositoryId });
        var applied = await UpsertIssuesAsync(db, repositoryId, [issue], milestones.ToDictionary(m => m.Number, m => m.Id), keepParent: true);
        if (applied.Count == 0)
        {
            var existing = await db.Scalar<Guid>(
                "select id from issues where tenant_id = @tenantId and github_issue_id = @githubId", new { githubId = issue.DatabaseId });
            await tx.CommitAsync(ct);
            return new MirroredIssue(existing, false);
        }

        var issueId = applied[0].Id;
        await ReplaceIssueChildrenAsync(db, repositoryId, applied, await LoadLabelIndexAsync(db, repositoryId));
        if (previous is { } p && p.State != issue.State)
            await db.Exec("""
                insert into issue_events (tenant_id, issue_id, kind, from_value, to_value, member_id)
                values (@tenantId, @issueId, @kind, @fromValue, @toValue, @memberId)
                """, new
            {
                issueId,
                kind = issue.State == "closed" ? "closed" : "reopened",
                fromValue = p.StateReason,
                toValue = issue.StateReason,
                memberId,
            });
        await tx.CommitAsync(ct);
        return new MirroredIssue(issueId, true);
    }

    // Returns false when the comment was not written: the issue is not in the mirror yet, or a newer copy is stored.
    public async Task<bool> ApplyWrittenCommentAsync(
        Guid tenantId, Guid repositoryId, SourceComment comment, CancellationToken ct)
    {
        await using var conn = await sync.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var db = new Db(conn, tx, tenantId, ct);
        await db.SetTenantAsync();

        await UpsertUsersAsync(db, [comment.Author]);
        var written = await UpsertCommentsAsync(db, repositoryId, [comment]);
        await tx.CommitAsync(ct);
        return written > 0;
    }

    private sealed record PreviousState(string State, string? StateReason);

    private sealed class Db(NpgsqlConnection conn, NpgsqlTransaction tx, Guid tenantId, CancellationToken ct)
    {
        public Task SetTenantAsync() =>
            Exec(TenantDb.SetTenantSql, new { tenantId = tenantId.ToString() }, withTenant: false);

        public Task<int> Exec(string sql, object? args = null, bool withTenant = true) =>
            conn.ExecuteAsync(new CommandDefinition(sql, Merge(args, withTenant), tx, cancellationToken: ct));

        public Task<T?> Scalar<T>(string sql, object? args = null) =>
            conn.QuerySingleOrDefaultAsync<T>(new CommandDefinition(sql, Merge(args, true), tx, cancellationToken: ct));

        public async Task<List<T>> Query<T>(string sql, object? args = null) =>
            (await conn.QueryAsync<T>(new CommandDefinition(sql, Merge(args, true), tx, cancellationToken: ct))).AsList();

        private DynamicParameters Merge(object? args, bool withTenant)
        {
            var p = new DynamicParameters(args);
            if (withTenant)
                p.Add("tenantId", tenantId);
            return p;
        }
    }

    // Postgres text cannot hold NUL.
    private static string? Clean(string? s) => s?.Replace("\0", "");

    private static DateTime Utc(DateTimeOffset value) => value.UtcDateTime;

    private static DateTime? Utc(DateTimeOffset? value) => value?.UtcDateTime;

    // Another row of the connection that still holds the incoming name (a repository deleted and recreated, or
    // renamed in the same list) is renamed out of the way and archived; it is rewritten if its own upsert follows.
    private static async Task<Guid> UpsertRepositoryAsync(Db db, Guid connectionId, SourceRepository repo)
    {
        var args = new
        {
            connectionId, githubId = repo.Id, name = repo.Name, fullName = repo.FullName, htmlUrl = repo.HtmlUrl, archived = repo.Archived,
        };
        await db.Exec("""
            update repositories
               set name = name || '#' || github_repo_id, full_name = full_name || '#' || github_repo_id, archived = true
             where tenant_id = @tenantId and connection_id = @connectionId and github_repo_id <> @githubId
               and (name = @name or full_name = @fullName)
            """, args);
        return await db.Scalar<Guid>("""
            insert into repositories (tenant_id, connection_id, github_repo_id, name, full_name, html_url, archived)
            values (@tenantId, @connectionId, @githubId, @name, @fullName, @htmlUrl, @archived)
            on conflict (tenant_id, github_repo_id) do update
               set connection_id = excluded.connection_id, name = excluded.name, full_name = excluded.full_name,
                   html_url = excluded.html_url, archived = excluded.archived
            returning id
            """, args);
    }

    private static IEnumerable<SourceUser?> SnapshotUsers(RepositorySnapshot snapshot)
    {
        foreach (var issue in snapshot.Issues)
        {
            yield return issue.Author;
            foreach (var a in issue.Assignees)
                yield return a;
        }
        foreach (var pr in snapshot.PullRequests)
        {
            yield return pr.Author;
            foreach (var r in pr.RequestedReviewers)
                yield return r;
        }
        foreach (var c in snapshot.Comments)
            yield return c.Author;
    }

    private static async Task UpsertUsersAsync(Db db, IEnumerable<SourceUser?> all)
    {
        var users = new Dictionary<long, SourceUser>();
        foreach (var u in all)
            if (u is { Id: { } id })
                users[id] = u;

        foreach (var (id, u) in users)
            await db.Exec("""
                insert into github_users (tenant_id, github_user_id, login, name, avatar_url)
                values (@tenantId, @id, @login, @name, @avatarUrl)
                on conflict (tenant_id, github_user_id) do update
                   set login = excluded.login, name = excluded.name, avatar_url = excluded.avatar_url, updated_at = now()
                 where (github_users.login, github_users.name, github_users.avatar_url)
                       is distinct from (excluded.login, excluded.name, excluded.avatar_url)
                """, new { id, login = u.Login, name = Clean(u.Name), avatarUrl = u.AvatarUrl });
    }

    private static async Task<Dictionary<int, Guid>> UpsertMilestonesAsync(
        Db db, Guid repoId, IReadOnlyList<SourceMilestone> milestones)
    {
        foreach (var m in milestones)
            await db.Exec("""
                insert into milestones (tenant_id, repository_id, github_milestone_id, number, title, state, due_on, closed_at, html_url)
                values (@tenantId, @repoId, @githubId, @number, @title, @state, @dueOn, @closedAt, @htmlUrl)
                on conflict (tenant_id, github_milestone_id) do update
                   set number = excluded.number, title = excluded.title, state = excluded.state,
                       due_on = excluded.due_on, closed_at = excluded.closed_at, html_url = excluded.html_url
                """, new
            {
                repoId, githubId = m.Id, number = m.Number, title = Clean(m.Title), state = m.State,
                dueOn = Utc(m.DueOn), closedAt = Utc(m.ClosedAt), htmlUrl = m.HtmlUrl,
            });

        await db.Exec(
            "delete from milestones where tenant_id = @tenantId and repository_id = @repoId and github_milestone_id <> all(@ids)",
            new { repoId, ids = milestones.Select(m => m.Id).ToArray() });

        var rows = await db.Query<(int Number, Guid Id)>(
            "select number, id from milestones where tenant_id = @tenantId and repository_id = @repoId", new { repoId });
        return rows.ToDictionary(r => r.Number, r => r.Id);
    }

    private sealed class LabelIndex
    {
        public Dictionary<long, Guid> ByGithubId { get; } = [];
        public Dictionary<string, Guid> ByName { get; } = new(StringComparer.Ordinal);
    }

    private static async Task<LabelIndex> UpsertLabelsAsync(Db db, Guid repoId, IReadOnlyList<SourceLabel> labels)
    {
        var withId = labels.Where(l => l.Id is not null).ToList();

        // A label first seen by name only (no GitHub id) takes its id over instead of duplicating the name.
        foreach (var l in withId)
        {
            await db.Exec("""
                update labels set github_label_id = @githubId
                 where tenant_id = @tenantId and repository_id = @repoId and name = @name and github_label_id is null
                   and not exists (select 1 from labels o where o.repository_id = @repoId and o.github_label_id = @githubId)
                """, new { repoId, githubId = l.Id, name = Clean(l.Name) });
            await db.Exec("""
                insert into labels (tenant_id, repository_id, github_label_id, name, color, description)
                values (@tenantId, @repoId, @githubId, @name, @color, @description)
                on conflict (repository_id, github_label_id) do update
                   set name = excluded.name, color = excluded.color, description = excluded.description
                """, new { repoId, githubId = l.Id, name = Clean(l.Name), color = l.Color, description = Clean(l.Description) });
        }

        await db.Exec("""
            delete from labels
             where tenant_id = @tenantId and repository_id = @repoId and github_label_id is not null and github_label_id <> all(@ids)
            """, new { repoId, ids = withId.Select(l => l.Id!.Value).ToArray() });

        return await LoadLabelIndexAsync(db, repoId);
    }

    private static async Task<LabelIndex> LoadLabelIndexAsync(Db db, Guid repoId)
    {
        var index = new LabelIndex();
        var rows = await db.Query<(Guid Id, long? GithubLabelId, string Name)>(
            "select id, github_label_id, name from labels where tenant_id = @tenantId and repository_id = @repoId", new { repoId });
        foreach (var r in rows)
        {
            if (r.GithubLabelId is { } gid)
                index.ByGithubId[gid] = r.Id;
            index.ByName[r.Name] = r.Id;
        }
        return index;
    }

    private static async Task<Guid> ResolveLabelAsync(Db db, Guid repoId, LabelIndex index, SourceLabel label)
    {
        if (label.Id is { } gid && index.ByGithubId.TryGetValue(gid, out var byId))
            return byId;
        var name = Clean(label.Name)!;
        if (index.ByName.TryGetValue(name, out var byName))
            return byName;

        var id = await db.Scalar<Guid>("""
            insert into labels (tenant_id, repository_id, github_label_id, name, color, description)
            values (@tenantId, @repoId, @githubId, @name, @color, @description)
            returning id
            """, new { repoId, githubId = label.Id, name, color = label.Color, description = Clean(label.Description) });
        index.ByName[name] = id;
        if (label.Id is { } newGid)
            index.ByGithubId[newGid] = id;
        return id;
    }

    private static async Task<List<(SourceIssue Issue, Guid Id)>> UpsertIssuesAsync(
        Db db, Guid repoId, IReadOnlyList<SourceIssue> issues, Dictionary<int, Guid> milestones, bool keepParent = false)
    {
        var applied = new List<(SourceIssue, Guid)>();
        foreach (var i in issues)
        {
            Guid? milestoneId = i.MilestoneNumber is { } n && milestones.TryGetValue(n, out var mid) ? mid : null;
            var id = await db.Scalar<Guid?>("""
                insert into issues (tenant_id, repository_id, github_issue_id, number, title, body, issue_type, state, state_reason,
                                    author_github_id, github_created_at, closed_at, github_updated_at, html_url, github_node_id,
                                    milestone_id, parent_github_issue_id)
                values (@tenantId, @repoId, @githubId, @number, @title, @body, @issueType, @state, @stateReason,
                        @author, @createdAt, @closedAt, @updatedAt, @htmlUrl, @nodeId, @milestoneId, @parentGithubId)
                on conflict (tenant_id, github_issue_id) do update
                   set repository_id = excluded.repository_id, number = excluded.number, title = excluded.title, body = excluded.body,
                       issue_type = excluded.issue_type, state = excluded.state, state_reason = excluded.state_reason,
                       author_github_id = excluded.author_github_id, github_created_at = excluded.github_created_at,
                       closed_at = excluded.closed_at, github_updated_at = excluded.github_updated_at, html_url = excluded.html_url,
                       github_node_id = excluded.github_node_id, milestone_id = excluded.milestone_id,
                       parent_github_issue_id = case when @keepParent then issues.parent_github_issue_id else excluded.parent_github_issue_id end
                 where excluded.github_updated_at >= issues.github_updated_at
                returning id
                """, new
            {
                repoId, githubId = i.DatabaseId, number = i.Number, title = Clean(i.Title), body = Clean(i.Body), issueType = i.IssueType,
                state = i.State, stateReason = i.StateReason, author = i.Author?.Id, createdAt = Utc(i.CreatedAt), closedAt = Utc(i.ClosedAt),
                updatedAt = Utc(i.UpdatedAt), htmlUrl = i.HtmlUrl, nodeId = i.NodeId, milestoneId, parentGithubId = i.ParentDatabaseId, keepParent,
            });
            if (id is { } issueId)
                applied.Add((i, issueId));
        }
        return applied;
    }

    private static async Task ReplaceIssueChildrenAsync(
        Db db, Guid repoId, List<(SourceIssue Issue, Guid Id)> issues, LabelIndex labels)
    {
        if (issues.Count == 0)
            return;
        var ids = issues.Select(i => i.Id).ToArray();
        await db.Exec("delete from issue_labels where tenant_id = @tenantId and issue_id = any(@ids)", new { ids });
        await db.Exec("delete from issue_assignees where tenant_id = @tenantId and issue_id = any(@ids)", new { ids });

        foreach (var (issue, id) in issues)
        {
            var labelIds = new HashSet<Guid>();
            foreach (var l in issue.Labels)
                labelIds.Add(await ResolveLabelAsync(db, repoId, labels, l));
            foreach (var labelId in labelIds)
                await db.Exec("insert into issue_labels (tenant_id, issue_id, label_id) values (@tenantId, @id, @labelId)",
                    new { id, labelId });

            foreach (var userId in issue.Assignees.Where(a => a.Id is not null).Select(a => a.Id!.Value).Distinct())
                await db.Exec("insert into issue_assignees (tenant_id, issue_id, github_user_id) values (@tenantId, @id, @userId)",
                    new { id, userId });
        }
    }

    // Second pass: parents are linked after every issue of the batch exists. Rows whose parent lives in a
    // repository synced later are picked up by the next run, whichever repository it is for.
    private static Task ResolveParentsAsync(Db db, List<(SourceIssue Issue, Guid Id)> issues) =>
        db.Exec("""
            update issues i
               set parent_issue_id = (select p.id from issues p
                                       where p.tenant_id = i.tenant_id and p.github_issue_id = i.parent_github_issue_id)
             where i.tenant_id = @tenantId
               and (i.id = any(@ids) or (i.parent_issue_id is null and i.parent_github_issue_id is not null))
               and i.parent_issue_id is distinct from (select p.id from issues p
                                                        where p.tenant_id = i.tenant_id and p.github_issue_id = i.parent_github_issue_id)
            """, new { ids = issues.Select(i => i.Id).ToArray() });

    private static async Task<List<(SourcePullRequest Pr, Guid Id)>> UpsertPullRequestsAsync(
        Db db, Guid repoId, IReadOnlyList<SourcePullRequest> pullRequests)
    {
        var applied = new List<(SourcePullRequest, Guid)>();
        foreach (var p in pullRequests)
        {
            var id = await db.Scalar<Guid?>("""
                insert into pull_requests (tenant_id, repository_id, github_pr_id, number, title, state, draft, review_requested,
                                           author_github_id, github_updated_at, html_url, github_node_id, merged_at)
                values (@tenantId, @repoId, @githubId, @number, @title, @state, @draft, @review,
                        @author, @updatedAt, @htmlUrl, @nodeId, @mergedAt)
                on conflict (tenant_id, github_pr_id) do update
                   set repository_id = excluded.repository_id, number = excluded.number, title = excluded.title, state = excluded.state,
                       draft = excluded.draft, review_requested = excluded.review_requested,
                       author_github_id = excluded.author_github_id, github_updated_at = excluded.github_updated_at,
                       html_url = excluded.html_url, github_node_id = excluded.github_node_id, merged_at = excluded.merged_at
                 where excluded.github_updated_at >= pull_requests.github_updated_at
                returning id
                """, new
            {
                repoId, githubId = p.DatabaseId, number = p.Number, title = Clean(p.Title), state = p.State, draft = p.Draft,
                review = p.ReviewRequested, author = p.Author?.Id, updatedAt = Utc(p.UpdatedAt), htmlUrl = p.HtmlUrl,
                nodeId = p.NodeId, mergedAt = Utc(p.MergedAt),
            });
            if (id is { } prId)
                applied.Add((p, prId));
        }
        return applied;
    }

    private static async Task ReplaceClosingLinksAsync(
        Db db, Guid repoId, List<(SourcePullRequest Pr, Guid Id)> pullRequests)
    {
        if (pullRequests.Count == 0)
            return;
        await db.Exec("delete from issue_pull_requests where tenant_id = @tenantId and pull_request_id = any(@ids)",
            new { ids = pullRequests.Select(p => p.Id).ToArray() });

        foreach (var (pr, id) in pullRequests)
        {
            var byGithubId = pr.ClosingIssues.Select(c => c.DatabaseId).ToArray();
            var byNumber = ClosingReferences.ParseIssueNumbers(pr.Body).ToArray();
            var issueIds = await db.Query<Guid>("""
                select id from issues
                 where tenant_id = @tenantId
                   and (github_issue_id = any(@byGithubId) or (repository_id = @repoId and number = any(@byNumber)))
                """, new { byGithubId, repoId, byNumber });
            foreach (var issueId in issueIds.Distinct())
                await db.Exec(
                    "insert into issue_pull_requests (tenant_id, issue_id, pull_request_id) values (@tenantId, @issueId, @id)",
                    new { issueId, id });
        }
    }

    private static async Task<int> UpsertCommentsAsync(Db db, Guid repoId, IReadOnlyList<SourceComment> comments)
    {
        if (comments.Count == 0)
            return 0;
        var numbers = comments.Select(c => c.IssueNumber).Distinct().ToArray();
        var issueIds = (await db.Query<(int Number, Guid Id)>(
                "select number, id from issues where tenant_id = @tenantId and repository_id = @repoId and number = any(@numbers)",
                new { repoId, numbers }))
            .ToDictionary(r => r.Number, r => r.Id);

        var count = 0;
        foreach (var c in comments)
        {
            if (!issueIds.TryGetValue(c.IssueNumber, out var issueId))
                continue;
            count += await db.Exec("""
                insert into comments (tenant_id, issue_id, github_comment_id, author_github_id, body, github_created_at, github_updated_at)
                values (@tenantId, @issueId, @githubId, @author, @body, @createdAt, @updatedAt)
                on conflict (tenant_id, github_comment_id) do update
                   set issue_id = excluded.issue_id, author_github_id = excluded.author_github_id, body = excluded.body,
                       github_created_at = excluded.github_created_at, github_updated_at = excluded.github_updated_at
                 where excluded.github_updated_at >= comments.github_updated_at
                """, new
            {
                issueId, githubId = c.Id, author = c.Author?.Id, body = Clean(c.Body) ?? "",
                createdAt = Utc(c.CreatedAt), updatedAt = Utc(c.UpdatedAt),
            });
        }
        return count;
    }
}
