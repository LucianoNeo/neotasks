using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
public sealed partial class ApiTests
{
    private static async Task<JsonElement> Created(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static async Task<(Guid Project, Guid Task, Guid Tracker)> UiWork(HttpClient c)
    {
        var project = (await Created(await c.PostAsJsonAsync("/app-api/projects", new { name = "API de pedidos" }))).GetProperty("id").GetGuid();
        var task = (await Created(await c.PostAsJsonAsync("/app-api/tasks", new { name = "Corrigir total", description = "Ajustar cálculo", projectId = project }))).GetProperty("id").GetGuid();
        var tasks = await c.GetFromJsonAsync<JsonElement>("/app-api/tasks");
        return (project, task, tasks[0].GetProperty("TimeTracker")[0].GetProperty("id").GetGuid());
    }
    [Fact]
    public async Task React_contract_supports_crud_and_rejects_stale_edits()
    {
        using var c = await Register("ui@example.test");
        var (project, task, tracker) = await UiWork(c);
        var projects = await c.GetFromJsonAsync<JsonElement>("/app-api/projects");
        Assert.Equal(task, projects[0].GetProperty("Tasks")[0].GetProperty("id").GetGuid());
        Assert.Equal("API de pedidos", projects[0].GetProperty("Tasks")[0].GetProperty("project").GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/app-api/tasks/{task}", new { name = "Editado", projectId = project, description = "Nova descrição", version = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/app-api/tasks/{task}", new { name = "Antigo", projectId = project, version = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.DeleteAsync($"/app-api/projects/{project}")).StatusCode);
        Assert.Equal("[]", await c.GetStringAsync("/app-api/tasks"));
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync($"/app-api/timetrackers/{tracker}", new { startDate = DateTimeOffset.UtcNow })).StatusCode);
    }
    [Fact]
    public async Task React_contract_isolates_tasks_trackers_and_collaborators()
    {
        using var a = await Register("uia@example.test"); using var b = await Register("uib@example.test");
        var (project, task, tracker) = await UiWork(a);
        Assert.Equal("[]", await b.GetStringAsync("/app-api/projects"));
        Assert.Equal("[]", await b.GetStringAsync("/app-api/tasks"));
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/app-api/projects/{project}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/app-api/tasks/{task}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync($"/app-api/timetrackers/{tracker}", new { startDate = DateTimeOffset.UtcNow })).StatusCode);
        var foreignUsers = await b.GetFromJsonAsync<JsonElement>("/app-api/collaborators");
        var foreignUser = foreignUsers[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsJsonAsync("/app-api/timetrackers", new { taskId = task, collaboratorId = foreignUser })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/app-api/tasktotalminutes/{task}")).StatusCode);
        Assert.DoesNotContain("password", foreignUsers.ToString(), StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task React_member_login_returns_role_and_enforces_owner_permissions()
    {
        using var owner = await Register("uiowner@example.test");
        await Created(await owner.PostAsJsonAsync("/app-api/collaborators", new { name = "Pessoa do time", email = "uimember@example.test", password = "TestPassword123!" }));
        using var member = factory.CreateClient();
        var login = await member.PostAsJsonAsync("/app-api/login", new { email = "uimember@example.test", password = "TestPassword123!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Member", session.GetProperty("role").GetString());
        Assert.Equal("Pessoa do time", session.GetProperty("username").GetString());
        member.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/app-api/projects", new { name = "Forbidden" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/app-api/collaborators", new { name = "Other", email = "other@example.test", password = "TestPassword123!" })).StatusCode);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/app-api/collaborators")).StatusCode);
    }
    [Fact]
    public async Task Timer_validation_and_local_day_totals_handle_midnight()
    {
        using var c = await Register("uitime@example.test");
        var (_, task, tracker) = await UiWork(c);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"/app-api/timetrackers/{tracker}", new { endDate = "2026-10-02T00:10:00-03:00" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/app-api/timetrackers/{tracker}", new { startDate = "2026-10-01T23:50:00-03:00" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/app-api/timetrackers/{tracker}", new { startDate = "2026-10-01T23:50:00-03:00" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"/app-api/timetrackers/{tracker}", new { endDate = "2026-10-03T23:50:01-03:00" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/app-api/timetrackers/{tracker}", new { endDate = "2026-10-02T00:10:00-03:00" })).StatusCode);
        var day = await c.PostAsJsonAsync("/app-api/daytotalminutes", new { daySent = "2026-10-02T12:00:00-03:00", offsetMinutes = -180 });
        Assert.Equal("00:10", await day.Content.ReadFromJsonAsync<string>());
        Assert.Equal(20.0, await c.GetFromJsonAsync<double>($"/app-api/tasktotalminutes/{task}"));
        var snapshot = await c.GetFromJsonAsync<JsonElement>("/app-api/tasks");
        Assert.EndsWith("Z", snapshot[0].GetProperty("TimeTracker")[0].GetProperty("startDate").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/app-api/timetrackers/{tracker}", new { endDate = "2026-10-02T00:20:00-03:00" })).StatusCode);
    }
    [Fact]
    public async Task Frontend_registration_authenticates_new_organization_owner()
    {
        using var c = factory.CreateClient();
        var registration = await Created(await c.PostAsJsonAsync("/app-api/register", new { name = "Reviewer", organization = "Team", email = "signup@example.test", password = "TestPassword123!" }));
        Assert.Equal("Owner", registration.GetProperty("role").GetString());
        Assert.Equal("Reviewer", registration.GetProperty("username").GetString());
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/app-api/validatetoken")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/app-api/register", new { name = "Reviewer", organization = "Team", email = "signup@example.test", password = "TestPassword123!" })).StatusCode);
    }
}
