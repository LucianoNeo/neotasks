using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

public sealed partial class ApiTests : IDisposable
{
    private readonly string database = "neotasks_test_" + Guid.NewGuid().ToString("N");
    private readonly string adminConnection = Environment.GetEnvironmentVariable("NEOTASKS_TEST_DATABASE") ?? "Host=localhost;Database=postgres;Username=postgres;Password=postgres";
    private readonly WebApplicationFactory<Program> factory;
    public ApiTests()
    {
        using var connection = new NpgsqlConnection(adminConnection); connection.Open();
        using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection); create.ExecuteNonQuery();
        var settings = new NpgsqlConnectionStringBuilder(adminConnection) { Database = database };
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development").UseSetting("ConnectionStrings:Database", settings.ConnectionString));
    }
    private async Task<HttpClient> Register(string email)
    {
        var c = factory.CreateClient();
        var r = await c.PostAsJsonAsync("/auth/register", new { organization = email, email, password = "TestPassword123!" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var data = await r.Content.ReadFromJsonAsync<JsonElement>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", data.GetProperty("token").GetString());
        return c;
    }
    private static async Task<Guid> Project(HttpClient c)
    {
        var r = await c.PostAsJsonAsync("/api/projects", new { name = "Quest board" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    private static async Task<Guid> Work(HttpClient c, Guid project)
    {
        var r = await c.PostAsJsonAsync($"/api/projects/{project}/tasks", new { title = "Build API" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    [Fact]
    public async Task Organization_cannot_read_or_write_another_organizations_tasks()
    {
        using var a = await Register("a@example.test"); using var b = await Register("b@example.test");
        var project = await Project(a); var task = await Work(a, project);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/projects/{project}/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync($"/api/projects/{project}/tasks", new { title = "Intrusion" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync($"/api/tasks/{task}", new { completed = true, version = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync($"/api/tasks/{task}/time", new { seconds = 60 })).StatusCode);
        Assert.Equal("[]", await b.GetStringAsync("/api/projects"));
    }
    [Fact]
    public async Task Member_cannot_create_projects_but_can_record_valid_time()
    {
        using var owner = await Register("owner@example.test");
        var project = await Project(owner); var task = await Work(owner, project);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/members", new { email = "member@example.test", password = "TestPassword123!" })).StatusCode);
        using var member = factory.CreateClient();
        var login = await member.PostAsJsonAsync("/auth/login", new { email = "member@example.test", password = "TestPassword123!" });
        member.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/projects", new { name = "Forbidden" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PostAsJsonAsync($"/api/tasks/{task}/time", new { seconds = -1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await member.PostAsJsonAsync($"/api/tasks/{task}/time", new { seconds = 3600 })).StatusCode);
    }
    [Fact]
    public async Task Stale_task_version_is_rejected()
    {
        using var c = await Register("version@example.test"); var task = await Work(c, await Project(c));
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/tasks/{task}", new { completed = true, version = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/tasks/{task}", new { completed = false, version = 1 })).StatusCode);
    }
    [Fact]
    public async Task Anonymous_and_invalid_login_are_rejected()
    {
        using var c = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/projects")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/auth/login", new { email = "missing@example.test", password = "wrong" })).StatusCode);
    }
    [Fact]
    public async Task Missing_credentials_return_client_errors()
    {
        using var c = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/auth/register", new { organization = "Test" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/auth/login", new { })).StatusCode);
    }
    public void Dispose()
    {
        factory.Dispose();
        using var connection = new NpgsqlConnection(adminConnection); connection.Open();
        using var drop = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", connection); drop.ExecuteNonQuery();
    }
}
