using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MediatR;
using NeoTasks.Service.Tasks;
using NeoTasks.Service.Workspace;

namespace NeoTasks;

public static class FrontendEndpoints
{
    private static Guid Org(ClaimsPrincipal actor) => Guid.Parse(actor.FindFirstValue("org")!);
    private static Guid Actor(ClaimsPrincipal actor) => Guid.Parse(actor.FindFirstValue("sub")!);
    private static IResult Error(string message, int status = 400) => Results.Json(new { error = message }, statusCode: status);
    private static bool ValidName(string? name, int length = 200) => !string.IsNullOrWhiteSpace(name) && name.Length <= length;

    public static void MapFrontendEndpoints(this WebApplication app, string signingKey)
    {
        app.MapPost("/app-api/login", async (FrontendLogin r, TasksDb db, PasswordHasher<User> hasher,AccountAccess access,HttpContext ctx) =>
        {
            var email = r.Email ?? r.Username;
            if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || r.Password is not { Length: > 0 and <= 128 }) return Error("E-mail ou senha inválidos.", 401);
            var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email.Trim().ToLowerInvariant());
            if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, r.Password) == PasswordVerificationResult.Failed) return Error("E-mail ou senha inválidos.", 401);
            await access.StartSession(user,ctx);
            return Results.Ok(new { token=access.Jwt(user), username = string.IsNullOrEmpty(user.Name) ? user.Email : user.Name, role = user.Role });
        }).RequireRateLimiting("access");

        var ui = app.MapGroup("/app-api").RequireAuthorization();
        ui.AddEndpointFilter<ValidationExceptionEndpointFilter>();
        ui.MapGet("/validatetoken", () => Results.Ok(new { valid = true }));
        ui.MapGet("/projects", async (int? page,string? q, HttpContext ctx,ClaimsPrincipal actor, TasksDb db) => {var query=db.Projects.Where(x=>x.OrganizationId==Org(actor));if(!string.IsNullOrWhiteSpace(q))query=query.Where(x=>x.Name.Contains(q));ctx.Response.Headers["X-Total-Count"]=(await query.CountAsync()).ToString();return Results.Ok((await Snapshot(actor,db,page??1,q,true)).Projects);});
        ui.MapGet("/tasks", async (int? page,string? q,string? filterBy,HttpContext ctx,ClaimsPrincipal actor, TasksDb db) => {var query=TaskQuery(Org(actor),db,q,filterBy);ctx.Response.Headers["X-Total-Count"]=(await query.CountAsync()).ToString();return Results.Ok((await Snapshot(actor,db,page??1,q,false,filterBy)).Tasks);});
        ui.MapGet("/counts", async (ClaimsPrincipal actor, ISender sender, CancellationToken cancellationToken) =>
        {
            var counts = await sender.Send(new GetDashboardCountsQuery(Org(actor)), cancellationToken);
            return Results.Ok(new { projects = counts.Projects, tasks = counts.Tasks, collaborators = counts.Collaborators });
        });
        ui.MapGet("/project-options",async(ClaimsPrincipal actor,TasksDb db)=>Results.Ok(await db.Projects.AsNoTracking().Where(p=>p.OrganizationId==Org(actor)).OrderBy(p=>p.Name).Select(p=>new{p.Id,p.Name}).ToListAsync()));
        ui.MapGet("/collaborators", async (ClaimsPrincipal actor, TasksDb db) =>
        {
            var org = Org(actor);
            return Results.Ok(await db.Users.AsNoTracking().Where(x => x.OrganizationId == org).OrderBy(x => x.Name)
                .Select(x => new { x.Id, name = x.Name == "" ? x.Email : x.Name, x.Email, x.Role }).ToListAsync());
        });
        ui.MapPost("/collaborators", async (MemberRequest r, ClaimsPrincipal actor, TasksDb db, PasswordHasher<User> hasher) =>
        {
            if (!ValidName(r.Name, 120) || string.IsNullOrWhiteSpace(r.Email) || r.Email.Length > 254 || !r.Email.Contains('@') || r.Password is not { Length: >= 12 and <= 128 }) return Error("Informe nome, e-mail e uma senha com pelo menos 12 caracteres.");
            var user = new User { OrganizationId = Org(actor), Name = r.Name!.Trim(), Email = r.Email.Trim().ToLowerInvariant() };
            user.PasswordHash = hasher.HashPassword(user, r.Password);
            db.Users.Add(user);
            try { await db.SaveChangesAsync(); } catch (DbUpdateException) { return Error("E-mail já cadastrado.", 409); }
            return Results.Created($"/app-api/collaborators/{user.Id}", new { user.Id, name = user.Name, user.Email, user.Role });
        }).RequireAuthorization(p => p.RequireRole("Owner"));

        ui.MapPost("/projects", async (ProjectRequest r, ClaimsPrincipal actor, TasksDb db) =>
        {
            if (!ValidName(r.Name, 120)) return Error("O nome do projeto deve ter entre 1 e 120 caracteres.");
            var project = new WorkProject { Name = r.Name.Trim(), OrganizationId = Org(actor) };
            db.Projects.Add(project); await db.SaveChangesAsync();
            return Results.Created($"/app-api/projects/{project.Id}", new { project.Id, project.Name });
        }).RequireAuthorization(p => p.RequireRole("Owner"));
        ui.MapPut("/projects/{id:guid}", async (Guid id, ProjectRequest r, ClaimsPrincipal actor, TasksDb db) =>
        {
            var org = Org(actor); var project = await db.Projects.SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == org);
            if (project is null) return Results.NotFound();
            if (!ValidName(r.Name, 120)) return Error("Nome de projeto inválido.");
            project.Name = r.Name.Trim(); await db.SaveChangesAsync(); return Results.Ok(new { project.Id, project.Name });
        }).RequireAuthorization(p => p.RequireRole("Owner"));
        ui.MapDelete("/projects/{id:guid}", async (Guid id, ClaimsPrincipal actor, TasksDb db) =>
        {
            var org = Org(actor); var project = await db.Projects.SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == org);
            if (project is null) return Results.NotFound();
            db.Projects.Remove(project); await db.SaveChangesAsync(); return Results.Ok(new { deleted = true });
        }).RequireAuthorization(p => p.RequireRole("Owner"));

        ui.MapPost("/tasks", async (FrontendTask r, ClaimsPrincipal actor, ISender sender, CancellationToken cancellationToken) =>
        {
            var taskId = await sender.Send(new CreateAssignedTaskCommand(
                Org(actor), Actor(actor), r.ProjectId, r.Name, r.Description, r.CollaboratorId, r.StartDate, r.EndDate), cancellationToken);
            return taskId is null ? Results.NotFound() : Results.Created($"/app-api/tasks/{taskId}", new { Id = taskId });
        });
        ui.MapPut("/tasks/{id:guid}", async (Guid id, FrontendTask r, ClaimsPrincipal actor, TasksDb db) =>
        {
            var org = Org(actor); var task = await db.Tasks.SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == org);
            if (task is null || !await db.Projects.AnyAsync(x => x.Id == r.ProjectId && x.OrganizationId == org)) return Results.NotFound();
            if (!ValidName(r.Name) || r.Description?.Length > 4000) return Error("Nome ou descrição da tarefa inválidos.");
            if (task.Version != r.Version) return Error("Esta tarefa mudou. Recarregue a página antes de editar.", 409);
            task.Title = r.Name.Trim(); task.Description = r.Description?.Trim() ?? ""; task.ProjectId = r.ProjectId; task.Version++;
            if (r.Completed.HasValue) task.Completed = r.Completed.Value;
            try { await db.SaveChangesAsync(); } catch (DbUpdateConcurrencyException) { return Error("A tarefa foi alterada por outra pessoa.", 409); }
            return Results.Ok(new { task.Id, task.Version });
        });
        ui.MapDelete("/tasks/{id:guid}", async (Guid id, ClaimsPrincipal actor, TasksDb db) =>
        {
            var org = Org(actor); var task = await db.Tasks.SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == org);
            if (task is null) return Results.NotFound();
            db.Tasks.Remove(task); await db.SaveChangesAsync(); return Results.Ok(new { deleted = true });
        });
        ui.MapPost("/timetrackers", async (FrontendTracker r, ClaimsPrincipal actor, TasksDb db) =>
        {
            var org = Org(actor);
            if (!await db.Tasks.AnyAsync(x => x.Id == r.TaskId && x.OrganizationId == org) || !await ValidCollaborator(r.CollaboratorId, org, db)) return Results.NotFound();
            if (!ValidDates(r.StartDate?.UtcDateTime, r.EndDate?.UtcDateTime)) return Error("Período inválido. O limite por apontamento é de 24 horas.");
            var entry = NewEntry(r.TaskId, actor, r.CollaboratorId, r.StartDate, r.EndDate);
            db.TimeEntries.Add(entry); await db.SaveChangesAsync(); return Results.Created($"/app-api/timetrackers/{entry.Id}", new { entry.Id });
        });
        ui.MapPut("/timetrackers/{id:guid}", async (Guid id, TrackerUpdate r, ClaimsPrincipal actor, TasksDb db) =>
        {
            var org = Org(actor); var entry = await db.TimeEntries.SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == org);
            if (entry is null || !await ValidCollaborator(r.CollaboratorId, org, db)) return Results.NotFound();
            if (r.StartDate.HasValue && entry.StartDate.HasValue) return Error("O apontamento já foi iniciado.", 409);
            if (r.EndDate.HasValue && entry.EndDate.HasValue) return Error("O apontamento já foi finalizado.", 409);
            var start = r.StartDate?.UtcDateTime ?? entry.StartDate; var end = r.EndDate?.UtcDateTime ?? entry.EndDate;
            if (!ValidDates(start, end)) return Error("Período inválido. O limite por apontamento é de 24 horas.");
            entry.StartDate = start; entry.EndDate = end;
            if (r.CollaboratorId.HasValue) entry.CollaboratorId = r.CollaboratorId;
            entry.Seconds = Duration(start, end); entry.Version++;
            try { await db.SaveChangesAsync(); } catch (DbUpdateConcurrencyException) { return Error("O apontamento mudou. Recarregue a página.", 409); }
            return Results.Ok(new { entry.Id });
        });
        ui.MapDelete("/timetrackers/{id:guid}", async (Guid id, ClaimsPrincipal actor, TasksDb db) =>
        {
            var org = Org(actor); var entry = await db.TimeEntries.SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == org);
            if (entry is null) return Results.NotFound();
            db.TimeEntries.Remove(entry); await db.SaveChangesAsync(); return Results.Ok(new { deleted = true });
        });
        ui.MapPost("/daytotalminutes", async (DayRequest r, ClaimsPrincipal actor, TasksDb db) =>
        {
            var offset = Math.Clamp(r.OffsetMinutes ?? 0, -840, 840);
            var localDay = r.DaySent?.ToOffset(TimeSpan.FromMinutes(offset)).Date ?? DateTime.UtcNow.AddMinutes(offset).Date;
            var start = DateTime.SpecifyKind(localDay, DateTimeKind.Utc).AddMinutes(-offset);
            return Results.Ok(FormatTime(await SecondsBetween(actor, db, start, start.AddDays(1))));
        });
        ui.MapGet("/monthtotalminutes", async (int? offsetMinutes, ClaimsPrincipal actor, TasksDb db) =>
        {
            var offset = Math.Clamp(offsetMinutes ?? 0, -840, 840); var now = DateTime.UtcNow.AddMinutes(offset);
            var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(-offset);
            return Results.Ok(FormatTime(await SecondsBetween(actor, db, start, start.AddMonths(1))));
        });
        ui.MapGet("/tasktotalminutes/{id:guid}", async (Guid id, ClaimsPrincipal actor, TasksDb db) =>
        {
            var org = Org(actor);
            if (!await db.Tasks.AnyAsync(x => x.Id == id && x.OrganizationId == org)) return Results.NotFound();
            return Results.Ok((await db.TimeEntries.Where(x => x.TaskId == id && x.OrganizationId == org).SumAsync(x => (long)x.Seconds)) / 60.0);
        });
    }

    private static async Task<bool> ValidCollaborator(Guid? id, Guid org, TasksDb db) => !id.HasValue || await db.Users.AnyAsync(x => x.Id == id && x.OrganizationId == org);
    private static bool ValidDates(DateTime? start, DateTime? end) => !end.HasValue || (start.HasValue && end > start && (end.Value - start.Value).TotalSeconds <= 86400);
    private static int Duration(DateTime? start, DateTime? end) => start.HasValue && end.HasValue ? (int)(end.Value - start.Value).TotalSeconds : 0;
    private static TimeEntry NewEntry(Guid task, ClaimsPrincipal actor, Guid? collaborator, DateTimeOffset? start, DateTimeOffset? end) => new()
    {
        TaskId = task, OrganizationId = Org(actor), UserId = Actor(actor), CollaboratorId = collaborator,
        StartDate = start?.UtcDateTime, EndDate = end?.UtcDateTime, Seconds = Duration(start?.UtcDateTime, end?.UtcDateTime)
    };
    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
    private static string FormatTime(long seconds) => $"{seconds / 3600:00}:{seconds % 3600 / 60:00}";
    private static async Task<long> SecondsBetween(ClaimsPrincipal actor, TasksDb db, DateTime start, DateTime end)
    {
        var org = Org(actor); var entries = await db.TimeEntries.AsNoTracking().Where(x => x.OrganizationId == org && x.StartDate != null && x.EndDate != null).ToListAsync();
        return entries.Sum(x => Math.Max(0L, (long)((x.EndDate!.Value < end ? x.EndDate.Value : end) - (x.StartDate!.Value > start ? x.StartDate.Value : start)).TotalSeconds));
    }
    private static IQueryable<WorkTask> TaskQuery(Guid org,TasksDb db,string? q,string? filterBy) {
        var query=db.Tasks.AsNoTracking().Where(x=>x.OrganizationId==org);
        if(!string.IsNullOrWhiteSpace(q)) {
            if(filterBy=="project")query=query.Where(t=>db.Projects.Any(p=>p.Id==t.ProjectId&&p.OrganizationId==org&&p.Name.Contains(q)));
            else if(filterBy=="collaborator")query=query.Where(t=>db.TimeEntries.Any(e=>e.TaskId==t.Id&&e.OrganizationId==org&&db.Users.Any(u=>u.Id==e.CollaboratorId&&u.OrganizationId==org&&u.Name.Contains(q))));
            else query=query.Where(t=>t.Title.Contains(q));
        }
        return query;
    }
    private static async Task<(List<ProjectView> Projects, List<TaskView> Tasks)> Snapshot(ClaimsPrincipal actor, TasksDb db,int page=1,string? q=null,bool projectPage=false,string? filterBy=null)
    {
        var org = Org(actor);
        var projectQuery=db.Projects.AsNoTracking().Where(x=>x.OrganizationId==org);
        if(projectPage&&!string.IsNullOrWhiteSpace(q))projectQuery=projectQuery.Where(x=>x.Name.Contains(q));
        var projects=await (projectPage?projectQuery.OrderBy(x=>x.Name).ThenBy(x=>x.Id).Skip((Math.Clamp(page,1,100000)-1)*20).Take(20):projectQuery).ToListAsync();
        var projectIds=projects.Select(p=>p.Id).ToArray();
        var taskQuery=TaskQuery(org,db,projectPage?null:q,filterBy);
        var tasks=await (projectPage?taskQuery.Where(t=>projectIds.Contains(t.ProjectId)):taskQuery.OrderBy(x=>x.Title).ThenBy(x=>x.Id).Skip((Math.Clamp(page,1,100000)-1)*20).Take(20)).ToListAsync();
        var taskIds=tasks.Select(t=>t.Id).ToArray();
        var entries = await db.TimeEntries.AsNoTracking().Where(x => x.OrganizationId == org&&taskIds.Contains(x.TaskId)).ToListAsync();
        var users = await db.Users.AsNoTracking().Where(x => x.OrganizationId == org).ToDictionaryAsync(x => x.Id);
        var projectMap = projects.ToDictionary(x => x.Id);
        var taskViews = tasks.Select(t => new TaskView(t.Id, t.Title, t.Description, new ProjectSummary(t.ProjectId, projectMap[t.ProjectId].Name),
            entries.Where(e => e.TaskId == t.Id).Select(e => new TrackerView(e.Id, AsUtc(e.StartDate), AsUtc(e.EndDate),
                e.CollaboratorId.HasValue && users.TryGetValue(e.CollaboratorId.Value, out var user) ? new CollaboratorView(user.Id, user.Name == "" ? user.Email : user.Name) : null)).ToList(), t.Version, t.Completed)).ToList();
        return (projects.Select(p => new ProjectView(p.Id, p.Name, taskViews.Where(t => t.Project.Id == p.Id).ToList())).ToList(), taskViews);
    }
}

public record FrontendLogin(string? Username, string? Email, string? Password);
public record FrontendTask(string Name, Guid ProjectId, string? Description, Guid? CollaboratorId, DateTimeOffset? StartDate, DateTimeOffset? EndDate, int Version = 0, bool? Completed = null);
public record FrontendTracker(Guid TaskId, Guid? CollaboratorId, DateTimeOffset? StartDate, DateTimeOffset? EndDate);
public record TrackerUpdate(DateTimeOffset? StartDate, DateTimeOffset? EndDate, Guid? CollaboratorId);
public record DayRequest(DateTimeOffset? DaySent, int? OffsetMinutes = null);
public record CollaboratorView(Guid Id, string Name);
public record TrackerView(Guid Id, DateTime? StartDate, DateTime? EndDate, CollaboratorView? Collaborator);
public record ProjectSummary(Guid Id, string Name);
public record TaskView(Guid Id, string Name, string Description, ProjectSummary Project, [property: JsonPropertyName("TimeTracker")] List<TrackerView> TimeTracker, int Version, bool Completed);
public record ProjectView(Guid Id, string Name, [property: JsonPropertyName("Tasks")] List<TaskView> Tasks);
