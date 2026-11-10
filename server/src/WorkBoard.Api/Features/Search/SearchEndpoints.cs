using WorkBoard.Api.Infrastructure.Data;

namespace WorkBoard.Api.Features.Search;

public sealed record IssueHit(Guid Id, string RepositoryName, int Number, string Title, string Status);

public sealed record CommentHit(Guid IssueId, string IssueTitle, string Snippet);

public sealed record MeetingNoteHit(Guid Id, string Title);

public sealed record SearchResult(
    IReadOnlyList<IssueHit> Issues,
    IReadOnlyList<CommentHit> Comments,
    IReadOnlyList<MeetingNoteHit> MeetingNotes);

public static class SearchEndpoints
{
    private const int MinQueryLength = 2;
    private const int MaxPerGroup = 20;
    private const int SnippetRadius = 40;

    public static void MapSearch(this RouteGroupBuilder api) =>
        api.MapGet("/search", async (string? q, TenantDb db, CancellationToken ct) =>
        {
            var term = q?.Trim() ?? "";
            if (term.Length < MinQueryLength)
                return new SearchResult([], [], []);

            return await db.ReadAsync(s => SearchAsync(s, term), ct);
        });

    private static async Task<SearchResult> SearchAsync(TenantSession s, string term)
    {
        var param = new { term, limit = MaxPerGroup };

        var issues = await s.QueryAsync<IssueRow>("""
            select i.id, r.name as repository_name, i.number, i.title, st.status
              from issues i
              join repositories r on r.id = i.repository_id
              join issue_status st on st.issue_id = i.id
             where position(lower(@term) in lower(i.title)) > 0
             order by i.github_updated_at desc, i.id
             limit @limit
            """, param);

        var comments = await s.QueryAsync<CommentRow>("""
            select c.issue_id, i.title as issue_title, c.body
              from comments c
              join issues i on i.id = c.issue_id
             where position(lower(@term) in lower(c.body)) > 0
             order by c.github_created_at desc, c.id
             limit @limit
            """, param);

        var notes = await s.QueryAsync<NoteRow>("""
            select n.id, n.title
              from meeting_notes n
             where position(lower(@term) in lower(n.title)) > 0
                or position(lower(@term) in lower(n.body_md)) > 0
             order by n.held_at desc, n.id
             limit @limit
            """, param);

        return new SearchResult(
            issues.Select(i => new IssueHit(i.Id, i.RepositoryName, i.Number, i.Title, i.Status)).ToList(),
            comments.Select(c => new CommentHit(c.IssueId, c.IssueTitle, Snippet(c.Body, term))).ToList(),
            notes.Select(n => new MeetingNoteHit(n.Id, n.Title)).ToList());
    }

    public static string Snippet(string body, string term)
    {
        var at = body.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
            return body.Length <= SnippetRadius * 2 ? body : body[..(SnippetRadius * 2)] + "…";

        var start = Math.Max(0, at - SnippetRadius);
        var end = Math.Min(body.Length, at + term.Length + SnippetRadius);
        return (start > 0 ? "…" : "") + body[start..end] + (end < body.Length ? "…" : "");
    }

    private sealed class IssueRow
    {
        public Guid Id { get; set; }
        public string RepositoryName { get; set; } = "";
        public int Number { get; set; }
        public string Title { get; set; } = "";
        public string Status { get; set; } = "";
    }

    private sealed class CommentRow
    {
        public Guid IssueId { get; set; }
        public string IssueTitle { get; set; } = "";
        public string Body { get; set; } = "";
    }

    private sealed class NoteRow
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
    }
}
