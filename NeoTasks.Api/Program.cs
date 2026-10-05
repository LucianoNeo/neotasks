using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NeoTasks;

var builder = WebApplication.CreateBuilder(args);
var signingKey = builder.Configuration["Jwt:Key"] ?? (builder.Environment.IsDevelopment() ? "local-demo-only-neotasks-key-32-characters" : throw new InvalidOperationException("Set Jwt__Key (32+ characters)."));
if (Encoding.UTF8.GetByteCount(signingKey) < 32) throw new InvalidOperationException("Jwt__Key must contain at least 32 bytes.");
builder.Services.AddDbContext<TasksDb>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Database") ?? "Host=localhost;Database=neotasks;Username=neotasks;Password=neotasks-local-only"));
builder.Services.AddScoped<PasswordHasher<User>>();
builder.Services.AddScoped<AccountAccess>();
builder.Services.AddRateLimiter(o=>{o.RejectionStatusCode=429;o.AddPolicy("access",c=>System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new System.Threading.RateLimiting.FixedWindowRateLimiterOptions{PermitLimit=30,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));});
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.Events=new JwtBearerEvents{OnTokenValidated=async c=>{var db=c.HttpContext.RequestServices.GetRequiredService<TasksDb>();var id=c.Principal?.FindFirstValue("sub");var u=Guid.TryParse(id,out var uid)?await db.Users.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==uid):null;if(u is null||u.SecurityStamp!=c.Principal?.FindFirstValue("stamp"))c.Fail("Revoked session");}};
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = "neotasks", ValidateAudience = true, ValidAudience = "neotasks-api",
        ValidateLifetime = true, ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
        NameClaimType = "sub", RoleClaimType = "role", ClockSkew = TimeSpan.FromSeconds(10)
    };
});
builder.Services.AddAuthorization();
var app = builder.Build();
app.UseExceptionHandler();
app.Use(async(ctx,next)=>{
    if(!HttpMethods.IsGet(ctx.Request.Method)&&ctx.Request.Headers.Origin.FirstOrDefault() is string origin&&(!Uri.TryCreate(origin,UriKind.Absolute,out var uri)||uri.Authority!=ctx.Request.Host.Value)){ctx.Response.StatusCode=403;return;}
    await next();
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async(ctx,next)=>{var db=ctx.RequestServices.GetRequiredService<TasksDb>();db.AuditActor=ctx.User.FindFirstValue("sub")??"anonymous";if(Guid.TryParse(ctx.User.FindFirstValue("org"),out var org))db.AuditOrganization=org;await next();});
using(var scope=app.Services.CreateScope())await SchemaUpgrade.Apply(scope.ServiceProvider.GetRequiredService<TasksDb>());
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

string Token(User u) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("neotasks", "neotasks-api",
    [new Claim("sub", u.Id.ToString()), new Claim("org", u.OrganizationId.ToString()), new Claim("role", u.Role),new Claim("stamp",u.SecurityStamp)],
    expires: DateTime.UtcNow.AddMinutes(30), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256)));
static Guid Org(ClaimsPrincipal u) => Guid.Parse(u.FindFirstValue("org")!);
static Guid Actor(ClaimsPrincipal u) => Guid.Parse(u.FindFirstValue("sub")!);
static bool ValidCredentials(string? email, string? password) => !string.IsNullOrWhiteSpace(email) && email.Length <= 254 && email.Contains('@') && password is { Length: >= 12 and <= 128 };

async Task<IResult> RegisterAccount(RegisterRequest r, TasksDb db, PasswordHasher<User> hasher,AccountAccess access,HttpContext ctx)
{
    if (string.IsNullOrWhiteSpace(r.Organization) || r.Organization.Length > 120 || r.Name?.Length > 120 || !ValidCredentials(r.Email, r.Password)) return Results.BadRequest(new { error = "Informe a organização e um e-mail válido. A senha deve ter entre 12 e 128 caracteres." });
    var email = r.Email.Trim().ToLowerInvariant();
    if (await db.Users.AnyAsync(x => x.Email == email)) return Results.Conflict(new { error = "Email already registered." });
    var org = new Organization { Name = r.Organization.Trim() };
    var user = new User { OrganizationId = org.Id, Email = email, Name = string.IsNullOrWhiteSpace(r.Name) ? email : r.Name.Trim(), Role = "Owner" };
    user.PasswordHash = hasher.HashPassword(user, r.Password);
    db.Organizations.Add(org); db.Users.Add(user);
    try { await db.SaveChangesAsync(); } catch (DbUpdateException) { return Results.Conflict(new { error = "Registration conflict." }); }
    await access.SendLink(user,"verify"); await access.StartSession(user,ctx);
    return Results.Created("/auth/login", new { token = Token(user), organizationId = org.Id, username = user.Name, role = user.Role });
}
app.MapPost("/auth/register", RegisterAccount).RequireRateLimiting("access");
app.MapPost("/app-api/register", RegisterAccount).RequireRateLimiting("access");
app.MapPost("/auth/login", async (LoginRequest r, TasksDb db, PasswordHasher<User> hasher,AccountAccess access,HttpContext ctx) =>
{
    if (string.IsNullOrWhiteSpace(r.Email) || string.IsNullOrEmpty(r.Password) || r.Password.Length > 128) return Results.Unauthorized();
    var user = await db.Users.SingleOrDefaultAsync(x => x.Email == r.Email.Trim().ToLowerInvariant());
    if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, r.Password) == PasswordVerificationResult.Failed) return Results.Unauthorized();
    await access.StartSession(user,ctx);return Results.Ok(new { token = Token(user) });
}).RequireRateLimiting("access");
var api = app.MapGroup("/api").RequireAuthorization();
api.MapPost("/members", async (MemberRequest r, ClaimsPrincipal actor, TasksDb db, PasswordHasher<User> hasher) =>
{
    if (!ValidCredentials(r.Email, r.Password)) return Results.BadRequest();
    if (r.Name?.Length > 120) return Results.BadRequest();
    var user = new User { OrganizationId = Org(actor), Email = r.Email.Trim().ToLowerInvariant(), Name = string.IsNullOrWhiteSpace(r.Name) ? r.Email.Trim() : r.Name.Trim() };
    user.PasswordHash = hasher.HashPassword(user, r.Password);
    db.Users.Add(user);
    try { await db.SaveChangesAsync(); } catch (DbUpdateException) { return Results.Conflict(); }
    return Results.Created($"/api/members/{user.Id}", new { user.Id, user.Name, user.Email, user.Role });
}).RequireAuthorization(p => p.RequireRole("Owner"));
api.MapGet("/projects", async (int? page, ClaimsPrincipal actor, TasksDb db) =>
{
    var p = Math.Clamp(page ?? 1, 1, 10000); var org = Org(actor);
    return Results.Ok(await db.Projects.AsNoTracking().Where(x => x.OrganizationId == org).OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((p - 1) * 20).Take(20).ToListAsync());
});
api.MapPost("/projects", async (ProjectRequest r, ClaimsPrincipal actor, TasksDb db) =>
{
    if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 120) return Results.BadRequest();
    var project = new WorkProject { OrganizationId = Org(actor), Name = r.Name.Trim() };
    db.Projects.Add(project); await db.SaveChangesAsync(); return Results.Created($"/api/projects/{project.Id}", project);
}).RequireAuthorization(p => p.RequireRole("Owner"));
api.MapPost("/projects/{id:guid}/tasks", async (Guid id, TaskRequest r, ClaimsPrincipal actor, TasksDb db) =>
{
    var org = Org(actor);
    if (!await db.Projects.AnyAsync(x => x.Id == id && x.OrganizationId == org)) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(r.Title) || r.Title.Length > 200) return Results.BadRequest();
    var task = new WorkTask { ProjectId = id, OrganizationId = org, Title = r.Title.Trim() };
    db.Tasks.Add(task); await db.SaveChangesAsync(); return Results.Created($"/api/tasks/{task.Id}", task);
});
api.MapGet("/projects/{id:guid}/tasks", async (Guid id, ClaimsPrincipal actor, TasksDb db) =>
{
    var org = Org(actor);
    if (!await db.Projects.AnyAsync(x => x.Id == id && x.OrganizationId == org)) return Results.NotFound();
    return Results.Ok(await db.Tasks.AsNoTracking().Where(x => x.ProjectId == id && x.OrganizationId == org).OrderBy(x => x.Title).Take(100).ToListAsync());
});
api.MapPut("/tasks/{id:guid}", async (Guid id, TaskUpdate r, ClaimsPrincipal actor, TasksDb db) =>
{
    var org = Org(actor); var task = await db.Tasks.SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == org);
    if (task is null) return Results.NotFound();
    if (task.Version != r.Version) return Results.Conflict(new { error = "Stale version. Reload the task." });
    task.Completed = r.Completed; task.Version++;
    try { await db.SaveChangesAsync(); } catch (DbUpdateConcurrencyException) { return Results.Conflict(); }
    return Results.Ok(task);
});
api.MapPost("/tasks/{id:guid}/time", async (Guid id, TimeRequest r, ClaimsPrincipal actor, TasksDb db) =>
{
    var org = Org(actor);
    if (!await db.Tasks.AnyAsync(x => x.Id == id && x.OrganizationId == org)) return Results.NotFound();
    if (r.Seconds is <= 0 or > 86400) return Results.BadRequest(new { error = "Seconds must be between 1 and 86400." });
    var entry = new TimeEntry { OrganizationId = org, TaskId = id, UserId = Actor(actor), Seconds = r.Seconds };
    db.TimeEntries.Add(entry); await db.SaveChangesAsync(); return Results.Created($"/api/time/{entry.Id}", entry);
});
app.MapFrontendEndpoints(signingKey);
app.MapAccessEndpoints();
app.Run();
public partial class Program { }
