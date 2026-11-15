namespace Djohnnie.SoftwareFactory.Tasks;

public sealed class GitHubTasksOptions
{
    public const string SectionName = "GitHub";

    /// <summary>
    /// The token the tasks read repositories with: nobody is signed in when a queued task runs, so they can't use a
    /// user's OAuth token. It needs read access to the contents of every repository that is analyzed.
    /// </summary>
    public string TasksToken { get; set; } = string.Empty;
}
