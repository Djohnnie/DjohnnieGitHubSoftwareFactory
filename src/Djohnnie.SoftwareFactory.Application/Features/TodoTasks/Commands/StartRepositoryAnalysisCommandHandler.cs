using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Application.Features.InternalAgents;
using Djohnnie.SoftwareFactory.Application.Features.RepositoryAnalysis;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.RepositoryAnalysis;
using Djohnnie.SoftwareFactory.Contracts.TodoTasks;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using MediatR;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Application.Features.TodoTasks;

public sealed class StartRepositoryAnalysisCommandHandler(
    ITodoTaskStore store,
    InternalAgentRegistry registry,
    BackgroundAgents agents,
    IOptions<TodoTaskOptions> options) : IRequestHandler<StartRepositoryAnalysisCommand, Response<TodoTask>>
{
    public async Task<Response<TodoTask>> Handle(StartRepositoryAnalysisCommand request, CancellationToken cancellationToken)
    {
        var dto = request.Dto;
        foreach (var key in new[] { InternalAgentKeys.RepositoryAnalyst, InternalAgentKeys.RepositoryWorkItemWriter })
        {
            var agent = await registry.GetAsync(key, cancellationToken);
            if (!agents.IsConfigured || !agent.IsEnabled)
            {
                throw new DomainRuleException($"The {agent.Name} agent is disabled or not configured.");
            }
        }
        if (await store.HasActiveRunAsync(dto.RepositoryFullName, TodoTaskKind.AnalyzeRepository, cancellationToken))
        {
            throw new DomainRuleException("This repository is already being analyzed. Wait for that run to end, or cancel it.");
        }

        var pathPrefix = RepositoryTreeFilter.NormalizePrefix(dto.PathPrefix);
        var payload = new AnalyzeRepositoryPayload(dto.Mode, pathPrefix, dto.MaxFeatures);
        var mode = dto.Mode == RepositoryAnalysisMode.Improvements ? "improvements" : "what exists";
        var title = pathPrefix is null ? $"Analyze the repository ({mode})" : $"Analyze '{pathPrefix}' ({mode})";
        var task = await store.CreateRunAsync(dto.RepositoryFullName, new NewTodoTask(TodoTaskKind.AnalyzeRepository, title, TodoTaskPayloads.Write(payload)),
            dto.RequestedBy, options.Value.MaxAttempts, cancellationToken);
        return new(task);
    }
}
