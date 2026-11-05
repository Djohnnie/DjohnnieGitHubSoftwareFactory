using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AgentTemplates;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentTemplates;

public sealed class GetTemplatesQueryHandler(IAgentTemplateStore store) : IRequestHandler<GetTemplatesQuery, Response<CollectionResult<AgentTemplate>>>
{
    public async Task<Response<CollectionResult<AgentTemplate>>> Handle(GetTemplatesQuery request, CancellationToken cancellationToken)
    {
        return new(new(await store.GetAllAsync(cancellationToken)));
    }
}
