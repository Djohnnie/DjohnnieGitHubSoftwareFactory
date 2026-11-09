using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalAgents;

/// <summary>Sends a short message to a model; a failure is a result for the user, not an error of the app.</summary>
public sealed class TestInternalAgentModelQueryHandler(
    AssistantAgentFactory factory,
    InternalAgentRegistry registry,
    IAgentUsageRecorder usageRecorder,
    ILogger<TestInternalAgentModelQueryHandler> logger)
    : IRequestHandler<TestInternalAgentModelQuery, Response<ModelTestResult>>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public async Task<Response<ModelTestResult>> Handle(TestInternalAgentModelQuery request, CancellationToken cancellationToken)
    {
        await registry.EnsureSeededAsync(cancellationToken);
        // The model under test may not be saved on the agent yet, and it is the one that has to be priced.
        var agent = await registry.GetAsync(request.Dto.Key, cancellationToken) with { Model = request.Dto.Model };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            await factory.TestModelAsync(request.Dto.Model, usage => usageRecorder.RecordAsync(agent, usage, InternalAgentUsageSource.ModelTest), timeout.Token);
            return new(new ModelTestResult(null));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(new ModelTestResult($"The model did not respond within {Timeout.TotalSeconds:0} seconds."));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The model {Model} failed its test.", request.Dto.Model);
            return new(new ModelTestResult(ex.Message));
        }
    }
}
