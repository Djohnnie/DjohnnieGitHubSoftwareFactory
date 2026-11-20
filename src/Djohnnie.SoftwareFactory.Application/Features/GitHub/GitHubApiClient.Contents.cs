using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Api = Djohnnie.SoftwareFactory.Contracts.GitHub.Api;
using Djohnnie.SoftwareFactory.Contracts.GitHub;

namespace Djohnnie.SoftwareFactory.Application.Features.GitHub;

/// <summary>Repository contents: directories, files, the tree and tarball, and commits of single files on the default branch.</summary>
public sealed partial class GitHubApiClient
{
    /// <summary>Lists the files in a repository directory, or an empty list when the directory does not exist.</summary>
    public async Task<IReadOnlyList<GitHubContentItem>> GetDirectoryAsync(string repositoryFullName, string path, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, ContentsUrl(repositoryFullName, path), null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<GitHubContentItem>>(cancellationToken) ?? [];
    }

    /// <summary>Gets a file from the default branch, or <c>null</c> when it does not exist.</summary>
    /// <exception cref="GitHubApiException">The path is a directory, symlink or submodule, or GitHub refused the request.</exception>
    public async Task<GitHubFile?> GetFileAsync(string repositoryFullName, string path, CancellationToken cancellationToken = default)
    {
        var url = ContentsUrl(repositoryFullName, path);
        using var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            // The contents API lists a directory instead of returning a file.
            throw new GitHubApiException(HttpStatusCode.UnprocessableEntity, $"'{path}' is a directory, not a file.");
        }

        var file = document.RootElement.Deserialize<Api.FileResponse>()
            ?? throw new GitHubApiException(response.StatusCode, "GitHub returned an empty response.");
        if (file.Type is { } type && type != "file")
        {
            throw new GitHubApiException(HttpStatusCode.UnprocessableEntity, $"'{path}' is a {type}, not a file.");
        }

        var content = file.Encoding == "base64" && file.Content is not null
            ? Encoding.UTF8.GetString(Convert.FromBase64String(file.Content.Replace("\n", string.Empty)))
            // GitHub leaves out the content of files over 1 MB (encoding "none"); the raw media type returns up to 100 MB.
            : await GetRawContentAsync(url, cancellationToken);
        return new GitHubFile(file.Path, file.Sha, content, file.HtmlUrl);
    }

    /// <summary>
    /// The whole file tree of the default branch with one request (Git Trees API, recursive). When GitHub truncates a
    /// very large tree, the entries it returned are kept and <see cref="GitHubTree.Truncated"/> is set.
    /// </summary>
    public async Task<GitHubTree> GetTreeAsync(string repositoryFullName, CancellationToken cancellationToken = default)
    {
        // HEAD resolves to the default branch, also when its name contains slashes.
        using var response = await SendAsync(HttpMethod.Get, $"repos/{repositoryFullName}/git/trees/HEAD?recursive=1", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var tree = await response.Content.ReadFromJsonAsync<Api.TreeResponse>(cancellationToken)
            ?? throw new GitHubApiException(response.StatusCode, "GitHub returned an empty response.");
        return new GitHubTree(tree.Sha, tree.Truncated, (tree.Tree ?? []).Select(e => new GitHubTreeEntry(e.Path, e.Type, e.Size)).ToList());
    }

    /// <summary>
    /// Downloads the whole default branch as one gzipped tar archive (one API request; GitHub redirects to its download
    /// host) and passes the stream to <paramref name="read"/> while it downloads, so the archive is never buffered.
    /// </summary>
    public async Task DownloadTarballAsync(string repositoryFullName, Func<Stream, Task> read, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"repos/{repositoryFullName}/tarball/HEAD");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokenProvider.GetAccessTokenAsync(cancellationToken));
        using var response = await SendRequestAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        ReportWhenUnauthorized(response);
        await EnsureSuccessAsync(response, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await read(stream);
    }

    private async Task<string> GetRawContentAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken, RawMediaType);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private const string RawMediaType = "application/vnd.github.raw+json";

    /// <summary>
    /// Creates or updates a file with a single commit on the default branch.
    /// Pass the current blob <paramref name="sha"/> to update; leave it <c>null</c> to create.
    /// </summary>
    public async Task<GitHubCommitResult> PutFileAsync(string repositoryFullName, string path, string content, string message, string? sha, CancellationToken cancellationToken = default)
    {
        var body = new Api.PutFileRequest(message, Convert.ToBase64String(Encoding.UTF8.GetBytes(content)), sha);
        using var response = await SendAsync(HttpMethod.Put, ContentsUrl(repositoryFullName, path), JsonContent.Create(body), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<Api.CommitResponse>(cancellationToken);
        return new GitHubCommitResult(result?.Content?.Sha, result?.Commit?.Sha ?? string.Empty, result?.Commit?.HtmlUrl);
    }

    /// <summary>Deletes a file with a single commit on the default branch.</summary>
    public async Task<GitHubCommitResult> DeleteFileAsync(string repositoryFullName, string path, string message, string sha, CancellationToken cancellationToken = default)
    {
        var body = new Api.DeleteFileRequest(message, sha);
        using var response = await SendAsync(HttpMethod.Delete, ContentsUrl(repositoryFullName, path), JsonContent.Create(body), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<Api.CommitResponse>(cancellationToken);
        return new GitHubCommitResult(null, result?.Commit?.Sha ?? string.Empty, result?.Commit?.HtmlUrl);
    }

    private static string ContentsUrl(string repositoryFullName, string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Uri collapses dot segments even when escaped, so '..' would leave the contents API (e.g. 'contents/../../../user').
        if (segments.Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException($"The path '{path}' can't contain '.' or '..' segments.", nameof(path));
        }

        return $"repos/{repositoryFullName}/contents/{string.Join('/', segments.Select(Uri.EscapeDataString))}";
    }
}
