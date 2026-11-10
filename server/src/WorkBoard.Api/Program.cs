using Dapper;
using WorkBoard.Api.Features.Connections;
using WorkBoard.Api.Features.Dev;
using WorkBoard.Api.Features.Health;
using WorkBoard.Api.Features.Issues;
using WorkBoard.Api.Features.Me;
using WorkBoard.Api.Features.MeetingNotes;
using WorkBoard.Api.Features.Overview;
using WorkBoard.Api.Features.Members;
using WorkBoard.Api.Features.Projects;
using WorkBoard.Api.Features.Search;
using WorkBoard.Api.Infrastructure.Data;
using WorkBoard.Api.Infrastructure.Identity;

DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWorkBoardData(builder.Configuration);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://localhost:3000")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseCors();

var open = app.MapGroup("/api");
open.MapHealth();
if (app.Environment.IsDevelopment())
    open.MapDevUsers();

var authed = app.MapGroup("/api").AddEndpointFilter<DevUserFilter>();
authed.MapMe();
authed.MapMembers();
authed.MapConnections();
authed.MapProjects();
authed.MapIssues();
authed.MapOverview();
authed.MapMeetingNotes();
authed.MapSearch();

app.Run();

public partial class Program;
