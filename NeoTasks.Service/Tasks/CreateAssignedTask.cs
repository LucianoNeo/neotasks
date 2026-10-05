using FluentValidation;
using MediatR;

namespace NeoTasks.Service.Tasks;

public sealed record CreateAssignedTaskCommand(
    Guid OrganizationId,
    Guid ActorId,
    Guid ProjectId,
    string Name,
    string? Description,
    Guid? CollaboratorId,
    DateTimeOffset? StartDate,
    DateTimeOffset? EndDate) : IRequest<Guid?>;

public interface ITaskCreationRepository
{
    Task<Guid?> CreateAssignedAsync(CreateAssignedTaskCommand command, CancellationToken cancellationToken);
}

public sealed class CreateAssignedTaskHandler(ITaskCreationRepository repository)
    : IRequestHandler<CreateAssignedTaskCommand, Guid?>
{
    public Task<Guid?> Handle(CreateAssignedTaskCommand request, CancellationToken cancellationToken) =>
        repository.CreateAssignedAsync(request, cancellationToken);
}

public sealed class CreateAssignedTaskValidator : AbstractValidator<CreateAssignedTaskCommand>
{
    public CreateAssignedTaskValidator()
    {
        RuleFor(command => command.OrganizationId).NotEmpty();
        RuleFor(command => command.ActorId).NotEmpty();
        RuleFor(command => command.ProjectId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Description).MaximumLength(4000);
        RuleFor(command => command.EndDate)
            .Must((command, end) => !end.HasValue || command.StartDate.HasValue && end > command.StartDate &&
                (end.Value - command.StartDate.Value).TotalHours <= 24)
            .WithMessage("O fim deve ser posterior ao início. Cada apontamento pode ter no máximo 24 horas.");
    }
}
