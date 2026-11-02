using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.AgentTemplates;
using Djohnnie.SoftwareFactory.Contracts.Common;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.AgentTemplates;

public sealed class TemplateHandlers(AgentTemplateStore store, AgentTemplateWorkflow workflow) :
    IRequestHandler<GetTemplatesQuery, Response<CollectionResult<AgentTemplate>>>,
    IRequestHandler<GetTemplateQuery, Response<AgentTemplate?>>,
    IRequestHandler<SaveTemplateCommand, Response<AgentTemplate>>,
    IRequestHandler<DeleteTemplateCommand, Response<OperationResult>>,
    IRequestHandler<ApplyTemplateCommand, Response<CollectionResult<AgentTemplateApplyResult>>>
{
    public async Task<Response<CollectionResult<AgentTemplate>>> Handle(GetTemplatesQuery request, CancellationToken cancellationToken) =>
        new(new(await store.GetAllAsync(cancellationToken)));

    public async Task<Response<AgentTemplate?>> Handle(GetTemplateQuery request, CancellationToken cancellationToken) =>
        new(await store.GetAsync(request.Dto.Id, cancellationToken));

    public async Task<Response<AgentTemplate>> Handle(SaveTemplateCommand request, CancellationToken cancellationToken) =>
        new(await workflow.SaveAsync(request.Dto.Id, request.Dto.FileName, request.Dto.Agent, cancellationToken));

    public async Task<Response<OperationResult>> Handle(DeleteTemplateCommand request, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(request.Dto.Id, cancellationToken);
        return new(new());
    }

    public async Task<Response<CollectionResult<AgentTemplateApplyResult>>> Handle(ApplyTemplateCommand request, CancellationToken cancellationToken) =>
        new(new(await workflow.ApplyAsync(request.Dto.Template, request.Dto.Repositories, request.Dto.FileName,
            request.Dto.Overwrite, request.Dto.CommitMessage, request.Progress, cancellationToken)));
}
