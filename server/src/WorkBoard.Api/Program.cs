using Dapper;
using WorkBoard.Api.Features.Connections;
using WorkBoard.Api.Features.Dev;
using WorkBoard.Api.Features.Health;
using WorkBoard.Api.Features.Issues;
using WorkBoard.Api.Features.Me;
using WorkBoard.Api.Features.MeetingNotes;
using WorkBoard.Api.Features.Milestones;
using WorkBoard.Api.Features.Overview;
using WorkBoard.Api.Features.Members;
using WorkBoard.Api.Features.Projects;
using WorkBoard.Api.Features.Search;
using WorkBoard.Api.Features.Sync;
using WorkBoard.Api.Features.Tenants;
using WorkBoard.Api.Infrastructure.Data;
using WorkBoard.Api.Infrastructure.Data.Migrations;
using WorkBoard.Api.Infrastructure.GitHub;
using WorkBoard.Api.Infrastructure.Identity;
using WorkBoard.Api.Infrastructure.Sync;

DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);

// Local-only values such as the Supabase service role key. The file is git-ignored.
if (builder.Environment.IsDevelopment())
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddWorkBoardData(builder.Configuration);
builder.Services.AddWorkBoardAuth(builder.Configuration);
builder.Services.AddGitHubApp(builder.Configuration);
builder.Services.AddWorkBoardSync(builder.Configuration);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://localhost:3000")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// `dotnet run -- migrate` applies migrations and exits; Development also migrates (and seeds a new DB) at startup.
var migrateOnly = args.Contains("migrate");
if (migrateOnly || app.Environment.IsDevelopment())
{
    try
    {
        await MigrationRunner.RunAsync(app.Configuration, seedFreshDatabase: app.Environment.IsDevelopment(),
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Migrations"));
    }
    catch (MigrationException e)
    {
        app.Logger.LogCritical("{Message}", e.Message);
        return 1;
    }
    if (migrateOnly)
        return 0;
}

app.UseCors();

var open = app.MapGroup("/api");
open.MapHealth();
if (app.Environment.IsDevelopment())
    open.MapDevUsers();
open.MapTenants();

var authed = app.MapGroup("/api").AddEndpointFilter<CurrentMemberFilter>();
authed.MapMe();
authed.MapMembers();
authed.MapConnections();
authed.MapProjects();
authed.MapIssues();
authed.MapMilestones();
authed.MapOverview();
authed.MapMeetingNotes();
authed.MapSearch();
authed.MapSync();

app.Run();
return 0;

public partial class Program;
