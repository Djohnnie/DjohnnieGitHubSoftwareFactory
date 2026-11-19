using System.Globalization;
using Djohnnie.SoftwareFactory.Contracts.AgentRuns;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.Logging;

namespace Djohnnie.SoftwareFactory.Worker;

/// <summary>One unattended session of a coding agent in a working directory.</summary>
public interface ICodingAgent
{
    Task<CodingAgentOutcome> RunAsync(CodingAgentRequest request, CancellationToken cancellationToken);
}

/// <param name="HomeDirectory">Where the agent runtime keeps its state for this session (<c>COPILOT_HOME</c>).</param>
/// <param name="Model">The model id, or <c>null</c> for the runtime's automatic model selection.</param>
/// <param name="CustomAgent">A custom agent from the repository's <c>.github/agents</c>, or <c>null</c> for the default agent.</param>
public sealed record CodingAgentRequest(
    string WorkingDirectory,
    string HomeDirectory,
    string Prompt,
    string Instructions,
    string? Model,
    CodingAgentProfile? CustomAgent,
    double? MaxAiCredits,
    TimeSpan Timeout);

/// <summary>A custom agent profile, read from the repository's agent file.</summary>
public sealed record CodingAgentProfile(string Name, string? DisplayName, string? Description, string Prompt, string? Model);

/// <param name="Summary">The agent's last message.</param>
/// <param name="StoppedEarly">Why the agent didn't finish (credit limit, timeout, an error), or <c>null</c> when it did.</param>
public sealed record CodingAgentOutcome(string? Summary, AgentRunUsage Usage, string? StoppedEarly);

/// <summary>
/// The GitHub Copilot SDK: starts the bundled Copilot runtime as a child process, runs one session with every tool approved
/// (the container is the sandbox) and reads the session's usage at the end, which is what the run cost in AI credits.
/// </summary>
public sealed class CopilotCodingAgent(ILogger<CopilotCodingAgent> logger, Microsoft.Extensions.Options.IOptions<AgentWorkerOptions> options) : ICodingAgent
{
    public async Task<CodingAgentOutcome> RunAsync(CodingAgentRequest request, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(request.HomeDirectory);
        await using var client = new CopilotClient(new CopilotClientOptions
        {
            GitHubToken = options.Value.EffectiveCopilotToken,
            UseLoggedInUser = false,
            BaseDirectory = request.HomeDirectory,
            WorkingDirectory = request.WorkingDirectory,
            // Replaces the inherited environment: the agent's shell must not see the worker's secrets.
            Environment = AgentEnvironment.Sanitized(System.Environment.GetEnvironmentVariables()),
            Logger = logger
        });
        await client.StartAsync(cancellationToken);

        string? stoppedEarly = null;
        CopilotSession? session = null;
        var config = new SessionConfig
        {
            ClientName = "DGHSF agent worker",
            WorkingDirectory = request.WorkingDirectory,
            Model = request.Model,
            OnPermissionRequest = PermissionHandler.ApproveAll,
            SystemMessage = new SystemMessageConfig { Mode = SystemMessageMode.Append, Content = request.Instructions },
#pragma warning disable GHCP001 // Session limits are experimental in the SDK; they are the only way to cap what one run spends.
            SessionLimits = request.MaxAiCredits is { } max ? new SessionLimitsConfig { MaxAiCredits = max } : null,
#pragma warning restore GHCP001
            EnableSessionStore = false,
            CoauthorEnabled = false,
            OnEvent = evt =>
            {
                switch (evt)
                {
                    case SessionLimitsExhaustedRequestedEvent limit:
                        // Nobody can raise the limit: refuse the model call, so the turn ends and the worker pushes what there is.
                        stoppedEarly = string.Create(CultureInfo.InvariantCulture,
                            $"The agent reached the limit of {limit.Data.MaxAiCredits:#,0.##} AI credits per run ({limit.Data.UsedAiCredits:#,0.##} used) before it finished.");
                        _ = RefuseMoreCreditsAsync(session, limit.Data.RequestId);
                        break;
                    case SessionErrorEvent error:
                        logger.LogWarning("The agent reported an error: {Message}", error.Data.Message);
                        stoppedEarly ??= $"The agent stopped with an error: {error.Data.Message}";
                        break;
                    case ToolExecutionStartEvent tool:
                        logger.LogInformation("Tool: {Tool}", tool.Data.ToolName);
                        break;
                }
            }
        };
        if (request.CustomAgent is { } agent)
        {
            config.CustomAgents =
            [
                new CustomAgentConfig
                {
                    Name = agent.Name,
                    DisplayName = agent.DisplayName,
                    Description = agent.Description,
                    Prompt = agent.Prompt,
                    Model = agent.Model
                }
            ];
            config.Agent = agent.Name;
        }

        await using (session = await client.CreateSessionAsync(config, cancellationToken))
        {
            string? summary = null;
            try
            {
                var reply = await session.SendAndWaitAsync(new MessageOptions { Prompt = request.Prompt },
                    request.Timeout, cancellationToken);
                summary = reply?.Data.Content;
            }
            catch (TimeoutException)
            {
                stoppedEarly = $"The agent didn't finish within {request.Timeout.TotalMinutes:0} minutes.";
                await AbortQuietlyAsync(session);
            }
            catch (OperationCanceledException)
            {
                await AbortQuietlyAsync(session);
                throw;
            }

            // The cost of the whole session, also when it stopped early: those credits were spent too.
            var usage = await GetUsageAsync(session);
            return new(summary, usage, stoppedEarly);
        }
    }

    private async Task RefuseMoreCreditsAsync(CopilotSession? session, string requestId)
    {
        if (session is null)
        {
            return;
        }
        try
        {
#pragma warning disable GHCP001 // Answering an exhausted session limit is experimental, like the limit itself.
            await session.Rpc.Ui.HandlePendingSessionLimitsExhaustedAsync(requestId,
                new UISessionLimitsExhaustedResponse { Action = UISessionLimitsExhaustedResponseAction.Cancel }, CancellationToken.None);
#pragma warning restore GHCP001
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Could not refuse more AI credits; aborting the turn instead.");
            await AbortQuietlyAsync(session);
        }
    }

    private async Task AbortQuietlyAsync(CopilotSession session)
    {
        try
        {
            await session.AbortAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            logger.LogWarning(ex, "Could not abort the agent's turn.");
        }
    }

    /// <summary>
    /// The session's accumulated usage. The SDK reports the cost in nano AI units; per its documentation 1,000,000,000 of
    /// them are one AI credit. When reading it fails, the run is recorded without a cost rather than failing.
    /// </summary>
    private async Task<AgentRunUsage> GetUsageAsync(CopilotSession session)
    {
        try
        {
#pragma warning disable GHCP001 // Usage metrics are experimental in the SDK; they are the only per-session cost there is.
            var metrics = await session.Rpc.Usage.GetMetricsAsync(CancellationToken.None);
#pragma warning restore GHCP001
            var models = metrics.ModelMetrics ?? new Dictionary<string, UsageMetricsModelMetric>();
            var complete = metrics.TotalNanoAiu is not null && (metrics.AiCreditsStatus is null || metrics.AiCreditsStatus == AiCreditsStatus.Complete);
            var perModel = models
                .OrderBy(m => m.Key, StringComparer.Ordinal)
                .Select(m => new AgentRunModelUsage(
                    Model: m.Key,
                    NanoAiu: (long)Math.Round((double)(m.Value.TotalNanoAiu ?? 0)),
                    Complete: m.Value.TotalNanoAiu is not null && (m.Value.AiCreditsStatus is null || m.Value.AiCreditsStatus == AiCreditsStatus.Complete),
                    Requests: (int)(m.Value.Requests?.Count ?? 0),
                    PremiumRequests: (decimal)(m.Value.Requests?.Cost ?? 0),
                    InputTokens: (long)(m.Value.Usage?.InputTokens ?? 0),
                    OutputTokens: (long)(m.Value.Usage?.OutputTokens ?? 0),
                    CacheReadTokens: (long)(m.Value.Usage?.CacheReadTokens ?? 0),
                    CacheWriteTokens: (long)(m.Value.Usage?.CacheWriteTokens ?? 0),
                    ReasoningTokens: (long)(m.Value.Usage?.ReasoningTokens ?? 0)))
                .ToList();
            if (perModel.Count == 0 && metrics.TotalNanoAiu is > 0 && !string.IsNullOrWhiteSpace(metrics.CurrentModel))
            {
                // No breakdown: the whole session is counted on the model it ended on.
                perModel.Add(new AgentRunModelUsage(metrics.CurrentModel, (long)Math.Round(metrics.TotalNanoAiu.Value), complete, 0,
                    (decimal)metrics.TotalPremiumRequestCost, 0, 0, 0, 0, 0));
            }
            return new AgentRunUsage(
                NanoAiu: (long)Math.Round(metrics.TotalNanoAiu ?? 0),
                Complete: complete,
                PremiumRequests: (decimal)metrics.TotalPremiumRequestCost,
                InputTokens: models.Values.Sum(m => m.Usage?.InputTokens ?? 0),
                OutputTokens: models.Values.Sum(m => m.Usage?.OutputTokens ?? 0),
                CacheReadTokens: models.Values.Sum(m => m.Usage?.CacheReadTokens ?? 0),
                CacheWriteTokens: models.Values.Sum(m => m.Usage?.CacheWriteTokens ?? 0),
                Models: models.Count == 0 ? metrics.CurrentModel : string.Join(", ", models.Keys.Order(StringComparer.Ordinal)),
                PerModel: perModel);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Could not read the session's usage.");
            return AgentRunUsage.None with { Complete = false };
        }
    }
}
