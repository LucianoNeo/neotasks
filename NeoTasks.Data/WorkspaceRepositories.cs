using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NeoTasks.Service.Tasks;
using NeoTasks.Service.Workspace;

namespace NeoTasks;

public sealed class DashboardCountsReader(TasksDb db) : IDashboardCountsReader
{
    public async Task<DashboardCounts> ReadAsync(Guid organizationId, CancellationToken cancellationToken) =>
        new(
            await db.Projects.CountAsync(project => project.OrganizationId == organizationId, cancellationToken),
            await db.Tasks.CountAsync(task => task.OrganizationId == organizationId, cancellationToken),
            await db.Users.CountAsync(user => user.OrganizationId == organizationId, cancellationToken));
}

public sealed class TaskCreationRepository(TasksDb db) : ITaskCreationRepository
{
    public async Task<Guid?> CreateAssignedAsync(CreateAssignedTaskCommand command, CancellationToken cancellationToken)
    {
        var project = await db.Projects.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == command.ProjectId && item.OrganizationId == command.OrganizationId, cancellationToken);
        if (project is null) return null;

        User? collaborator = null;
        if (command.CollaboratorId is Guid collaboratorId)
        {
            collaborator = await db.Users.AsNoTracking()
                .SingleOrDefaultAsync(user => user.Id == collaboratorId && user.OrganizationId == command.OrganizationId, cancellationToken);
            if (collaborator is null) return null;
        }

        var task = new WorkTask
        {
            OrganizationId = command.OrganizationId,
            ProjectId = command.ProjectId,
            Title = command.Name.Trim(),
            Description = command.Description?.Trim() ?? ""
        };
        var start = command.StartDate?.UtcDateTime;
        var end = command.EndDate?.UtcDateTime;
        var entry = new TimeEntry
        {
            TaskId = task.Id,
            OrganizationId = command.OrganizationId,
            UserId = command.ActorId,
            CollaboratorId = command.CollaboratorId,
            StartDate = start,
            EndDate = end,
            Seconds = start.HasValue && end.HasValue ? (int)(end.Value - start.Value).TotalSeconds : 0
        };
        db.Tasks.Add(task);
        db.TimeEntries.Add(entry);

        if (collaborator is not null)
        {
            db.Outbox.Add(new OutboxMessage
            {
                EventType = "task.assigned.v1",
                Payload = JsonSerializer.Serialize(new
                {
                    recipient = collaborator.Email,
                    recipientName = collaborator.Name,
                    taskTitle = task.Title,
                    projectName = project.Name
                })
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return task.Id;
    }
}
