using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;

namespace NeoTasks.Service.Workspace;

public sealed record DashboardCounts(int Projects, int Tasks, int Collaborators);
public sealed record GetDashboardCountsQuery(Guid OrganizationId) : IRequest<DashboardCounts>;

public interface IDashboardCountsReader
{
    Task<DashboardCounts> ReadAsync(Guid organizationId, CancellationToken cancellationToken);
}

public sealed class GetDashboardCountsHandler(IDashboardCountsReader reader, IDistributedCache cache)
    : IRequestHandler<GetDashboardCountsQuery, DashboardCounts>
{
    public async Task<DashboardCounts> Handle(GetDashboardCountsQuery request, CancellationToken cancellationToken)
    {
        var key = $"neotasks:dashboard:{request.OrganizationId:N}";
        try
        {
            var cached = await cache.GetStringAsync(key, cancellationToken);
            if (cached is not null) return JsonSerializer.Deserialize<DashboardCounts>(cached)!;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { }

        var counts = await reader.ReadAsync(request.OrganizationId, cancellationToken);
        try
        {
            await cache.SetStringAsync(key, JsonSerializer.Serialize(counts), new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { }

        return counts;
    }
}

public sealed class GetDashboardCountsValidator : AbstractValidator<GetDashboardCountsQuery>
{
    public GetDashboardCountsValidator() => RuleFor(query => query.OrganizationId).NotEmpty();
}
