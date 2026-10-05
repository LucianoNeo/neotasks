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

public sealed class AccessToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Hash { get; set; } = "";
    public string Purpose { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public bool Used { get; set; }
}

public sealed class AuditRecord
{
    public long Id { get; set; }
    public Guid? OrganizationId { get; set; }
    public string Actor { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Resource { get; set; } = "";
    public string ResourceId { get; set; } = "";
    public DateTime At { get; set; } = DateTime.UtcNow;
}

public record RegisterRequest(string Organization, string Email, string Password, string? Name = null);
public record LoginRequest(string Email, string Password);
public record MemberRequest(string Email, string Password, string? Name = null);
public record ProjectRequest(string Name);
public record TaskRequest(string Title);
public record TaskUpdate(bool Completed, int Version);
public record TimeRequest(int Seconds);
