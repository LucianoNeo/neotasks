using Microsoft.EntityFrameworkCore;

namespace NeoTasks;

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Member";
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public bool EmailVerified { get; set; }
}
public sealed class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
}
public sealed class WorkProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
}
public sealed class WorkTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Completed { get; set; }
    public int Version { get; set; } = 1;
}
public sealed class TimeEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid TaskId { get; set; }
    public Guid UserId { get; set; }
    public int Seconds { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public Guid? CollaboratorId { get; set; }
    public int Version { get; set; } = 1;
}
public sealed class TasksDb(DbContextOptions<TasksDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<WorkProject> Projects => Set<WorkProject>();
    public DbSet<WorkTask> Tasks => Set<WorkTask>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
    public DbSet<AccessToken> AccessTokens => Set<AccessToken>();
    public DbSet<AuditRecord> Audit => Set<AuditRecord>();
    public Guid? AuditOrganization { get; set; }
    public string AuditActor { get; set; } = "system";
    public override async Task<int> SaveChangesAsync(CancellationToken ct=default) {
        var changes=ChangeTracker.Entries().Where(e=>e.Entity is not AuditRecord && e.Entity is not AccessToken && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).Select(e=>new AuditRecord { OrganizationId=AuditOrganization ?? (e.Entity is User u?u.OrganizationId:e.Entity is Organization o?o.Id:null),Actor=AuditActor,Operation=e.State.ToString(),Resource=e.Metadata.ClrType.Name,ResourceId=e.Properties.FirstOrDefault(p=>p.Metadata.IsPrimaryKey())?.CurrentValue?.ToString()??"" }).ToArray();
        Audit.AddRange(changes);return await base.SaveChangesAsync(ct);
    }
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AccessToken>().HasIndex(x=>x.Hash).IsUnique();
        b.Entity<AccessToken>().HasOne<User>().WithMany().HasForeignKey(x=>x.UserId);
        b.Entity<AuditRecord>().HasIndex(x=>new{x.OrganizationId,x.Id});
        b.Entity<User>().HasIndex(x => x.Email).IsUnique();
        b.Entity<User>().HasAlternateKey(x => new { x.Id, x.OrganizationId });
        b.Entity<User>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId);
        b.Entity<WorkProject>().HasAlternateKey(x => new { x.Id, x.OrganizationId });
        b.Entity<WorkProject>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId);
        b.Entity<WorkTask>().HasOne<WorkProject>().WithMany().HasForeignKey(x => new { x.ProjectId, x.OrganizationId }).HasPrincipalKey(x => new { x.Id, x.OrganizationId });
        b.Entity<WorkTask>().HasAlternateKey(x => new { x.Id, x.OrganizationId });
        b.Entity<WorkTask>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<TimeEntry>().HasOne<WorkTask>().WithMany().HasForeignKey(x => new { x.TaskId, x.OrganizationId }).HasPrincipalKey(x => new { x.Id, x.OrganizationId });
        b.Entity<TimeEntry>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<TimeEntry>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<TimeEntry>().HasOne<User>().WithMany().HasForeignKey(x => new { x.CollaboratorId, x.OrganizationId }).HasPrincipalKey(x => new { x.Id, x.OrganizationId });
    }
}
public record RegisterRequest(string Organization, string Email, string Password, string? Name = null);
public record LoginRequest(string Email, string Password);
public record MemberRequest(string Email, string Password, string? Name = null);
public record ProjectRequest(string Name);
public record TaskRequest(string Title);
public record TaskUpdate(bool Completed, int Version);
public record TimeRequest(int Seconds);

public sealed class AccessToken {
 public Guid Id{get;set;}=Guid.NewGuid();public Guid UserId{get;set;}public string Hash{get;set;}="";public string Purpose{get;set;}="";public DateTime ExpiresAt{get;set;}public bool Used{get;set;}
}
public sealed class AuditRecord {
 public long Id{get;set;}public Guid? OrganizationId{get;set;}public string Actor{get;set;}="";public string Operation{get;set;}="";public string Resource{get;set;}="";public string ResourceId{get;set;}="";public DateTime At{get;set;}=DateTime.UtcNow;
}
