using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AgentTemplates;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentTemplates;

public sealed class GetTemplateQueryHandler(IAgentTemplateStore store) : IRequestHandler<GetTemplateQuery, Response<AgentTemplate?>>
{
    public async Task<Response<AgentTemplate?>> Handle(GetTemplateQuery request, CancellationToken cancellationToken)
    {
        return new(await store.GetAsync(request.Dto.Id, cancellationToken));
    }
}
