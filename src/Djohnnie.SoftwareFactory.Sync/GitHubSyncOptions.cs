namespace Djohnnie.SoftwareFactory.Sync;

public sealed class GitHubSyncOptions
{
    public const string SectionName = "GitHub";

    /// <summary>The token the sync reads issues with: nobody is signed in when the timer fires, so it can't use a user's OAuth token.</summary>
    public string SyncToken { get; set; } = string.Empty;
}
