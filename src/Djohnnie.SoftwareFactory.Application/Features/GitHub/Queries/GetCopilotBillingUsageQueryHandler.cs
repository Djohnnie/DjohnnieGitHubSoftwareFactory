using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

public sealed class GetCopilotBillingUsageQueryHandler(GitHubApiClient gitHub, TimeProvider time) : IRequestHandler<GetCopilotBillingUsageQuery, Response<CopilotBillingUsage>>
{
    public async Task<Response<CopilotBillingUsage>> Handle(GetCopilotBillingUsageQuery request, CancellationToken cancellationToken)
    {
        var account = request.Dto.RepositoryFullName.Split('/')[0];
        // GitHub's billing periods are calendar months in UTC.
        var now = time.GetUtcNow().UtcDateTime;
        var items = await gitHub.GetCopilotBillingUsageAsync(account, request.Dto.OwnedByOrganization, request.Dto.Report, now.Year, now.Month, cancellationToken);
        return new(new CopilotBillingUsage(account, now.Year, now.Month, items));
    }
}
