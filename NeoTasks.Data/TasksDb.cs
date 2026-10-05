using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace NeoTasks;

public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventType { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAtUtc { get; set; }
}

public sealed class TasksDb(DbContextOptions<TasksDb> options, IDistributedCache? cache = null) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<WorkProject> Projects => Set<WorkProject>();
    public DbSet<WorkTask> Tasks => Set<WorkTask>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
    public DbSet<AccessToken> AccessTokens => Set<AccessToken>();
    public DbSet<AuditRecord> Audit => Set<AuditRecord>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public Guid? AuditOrganization { get; set; }
    public string AuditActor { get; set; } = "system";

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.Entity is not AuditRecord && e.Entity is not AccessToken && e.Entity is not OutboxMessage &&
                        e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToArray();
        var changes = entries.Select(e => new AuditRecord
        {
            OrganizationId = AuditOrganization ?? (e.Entity is User user ? user.OrganizationId : e.Entity is Organization organization ? organization.Id : null),
            Actor = AuditActor,
            Operation = e.State.ToString(),
            Resource = e.Metadata.ClrType.Name,
            ResourceId = e.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue?.ToString() ?? ""
        }).ToArray();
        Audit.AddRange(changes);

        var affectedOrganizations = entries
            .Select(e => e.Properties.FirstOrDefault(p => p.Metadata.Name == nameof(User.OrganizationId) || p.Metadata.Name == "OrganizationId")?.CurrentValue)
            .OfType<Guid>().Distinct().ToArray();
        var saved = await base.SaveChangesAsync(ct);

        // Redis is an optimization. A cache outage must not turn a committed write into an API error.
        if (cache is not null)
        {
            foreach (var organizationId in affectedOrganizations)
            {
                try { await cache.RemoveAsync($"neotasks:dashboard:{organizationId:N}", ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { }
            }
        }
        return saved;
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AccessToken>().HasIndex(x => x.Hash).IsUnique();
        b.Entity<AccessToken>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<AuditRecord>().HasIndex(x => new { x.OrganizationId, x.Id });
        b.Entity<OutboxMessage>().HasIndex(x => new { x.PublishedAtUtc, x.OccurredAtUtc });
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
