namespace Djohnnie.SoftwareFactory.Application.Features.AgentRuns;

/// <summary>
/// The first line of everything the DGHSF agent worker posts on GitHub. GitHub shows the token's account as the author, so the
/// header says that DGHSF wrote it on that account's behalf; DGHSF's own pages recognise it to show who really wrote it.
/// </summary>
public static class DghsfHeader
{
    private const string Prefix = "<sub>🤖 **DGHSF**";
    private const string End = "</sub>";

    /// <summary>The header with a blank line after it; without a known account it only names DGHSF.</summary>
    public static string For(string? onBehalfOf)
    {
        return string.IsNullOrWhiteSpace(onBehalfOf)
            ? $"{Prefix}{End}\n\n"
            : $"{Prefix} on behalf of [{onBehalfOf}](https://github.com/{onBehalfOf}){End}\n\n";
    }

    /// <summary>Whether the DGHSF agent worker wrote <paramref name="body"/>: it starts with the header or carries the worker's mark.</summary>
    public static bool IsFromDghsf(string? body)
    {
        return body is not null && (body.TrimStart().StartsWith(Prefix, StringComparison.Ordinal) || PullRequestDiscussion.IsFromWorker(body));
    }

    /// <summary>The body without the header, which DGHSF's pages show as the author instead; other bodies stay as they are.</summary>
    public static string Strip(string body)
    {
        var trimmed = body.TrimStart();
        if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return body;
        }
        var end = trimmed.IndexOf(End, Prefix.Length, StringComparison.Ordinal);
        return end < 0 ? body : trimmed[(end + End.Length)..].TrimStart();
    }
}
