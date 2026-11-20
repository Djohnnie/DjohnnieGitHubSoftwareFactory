namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

/// <summary>The base address and headers of every HttpClient that calls GitHub's REST API.</summary>
public static class GitHubHttpDefaults
{
    public static readonly Uri BaseAddress = new("https://api.github.com/");
    public const string UserAgent = "DGHSF/1.0";
    public const string MediaType = "application/vnd.github+json";

    public static void Apply(HttpClient client)
    {
        client.BaseAddress = BaseAddress;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd(MediaType);
    }
}
