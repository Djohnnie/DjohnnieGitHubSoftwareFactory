namespace Djohnnie.SoftwareFactory.Worker;

/// <summary>How the DGHSF agent worker clones, runs the agent and pushes (section <c>AgentWorker</c>).</summary>
public sealed class AgentWorkerOptions
{
    public const string SectionName = "AgentWorker";

    /// <summary>
    /// The token the worker clones, pushes, opens pull requests and reads work items with: nobody is signed in when a job
    /// runs. It needs read and write access to contents and pull requests, and read access to issues.
    /// </summary>
    public string GitHubToken { get; set; } = string.Empty;

    /// <summary>
    /// The token the Copilot SDK authenticates with, whose Copilot plan pays for the AI credits: a fine-grained token with
    /// the Copilot Requests permission (classic tokens aren't accepted). Empty to use <see cref="GitHubToken"/>.
    /// </summary>
    public string? CopilotToken { get; set; }

    /// <summary>Where the worker clones repositories; every run gets its own folder, deleted when the run ends.</summary>
    public string? WorkDirectory { get; set; }

    /// <summary>The most AI credits one session of the agent may use (a soft limit, checked between model calls); empty for no limit.</summary>
    public double? MaxAiCreditsPerRun { get; set; }

    /// <summary>
    /// How long the agent may work. Keep it below the job's replica timeout, so the worker still has time to push what the
    /// agent did as a draft pull request.
    /// </summary>
    public TimeSpan RunTimeout { get; set; } = TimeSpan.FromMinutes(60);

    /// <summary>How long a job execution waits for a message before it ends without doing anything.</summary>
    public TimeSpan ReceiveTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public string CommitAuthorName { get; set; } = "DGHSF agent worker";
    public string CommitAuthorEmail { get; set; } = "dghsf-agent-worker@users.noreply.github.com";

    /// <summary>Whether every pull request starts as a draft; otherwise only the work of an agent that stopped early is a draft.</summary>
    public bool DraftPullRequests { get; set; }

    public string EffectiveCopilotToken
    {
        get
        {
            return string.IsNullOrWhiteSpace(CopilotToken) ? GitHubToken : CopilotToken;
        }
    }

    public string EffectiveWorkDirectory
    {
        get
        {
            return string.IsNullOrWhiteSpace(WorkDirectory) ? Path.Combine(Path.GetTempPath(), "dghsf-agent-worker") : WorkDirectory;
        }
    }
}
