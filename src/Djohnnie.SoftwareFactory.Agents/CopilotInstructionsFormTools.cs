using System.ComponentModel;
using Djohnnie.SoftwareFactory.Contracts.Assistant;
using Djohnnie.SoftwareFactory.Contracts.CopilotInstructions;
using Microsoft.Extensions.AI;

namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>
/// Tools that let the Copilot Instructions Copilot explore the selected repository (read-only) and prefill the
/// <c>.github/copilot-instructions.md</c> editor. The file is only changed in the UI; the user still saves and commits it.
/// </summary>
/// <param name="getForm">The current state of the editor.</param>
/// <param name="setContent">Replaces the content in the editor.</param>
/// <param name="listDirectory">Lists a directory of the repository (empty path = root) on the default branch.</param>
/// <param name="readFile">Reads a file of the repository on the default branch, or <c>null</c> when it does not exist.</param>
/// <param name="onUi">Runs a change on the UI thread and re-renders the page.</param>
/// <param name="gitHubTools">Read-only GitHub MCP tools pinned to the repository, or <c>null</c> when not connected.</param>
public sealed class CopilotInstructionsFormTools(
    Func<CopilotInstructionsFormSnapshot> getForm,
    Action<string> setContent,
    Func<string, CancellationToken, Task<IReadOnlyList<RepositoryEntry>>> listDirectory,
    Func<string, CancellationToken, Task<string?>> readFile,
    Func<Action, Task> onUi,
    IReadOnlyList<AITool>? gitHubTools = null)
{
    /// <summary>Files longer than this are cut off, so a single file doesn't flood the conversation.</summary>
    public const int MaxFileLength = RepositoryFileTools.MaxFileLength;

    private readonly RepositoryFileTools _files = new(listDirectory, readFile);

    public const int MaxContentLength = 50_000;

    public static readonly string Instructions = $"""
        ## Your task: build the repository's Copilot instructions file
        The user is on the Copilot instructions page of the selected repository. They want your help to write or improve
        `{CopilotInstructionsFile.Path}`: the repository-wide custom instructions that GitHub Copilot (chat, the Copilot cloud
        agent and code review) adds to every request in this repository. When they save, DGHSF commits the file directly to
        the default branch.

        How to work:
        1. Call `get_instructions_file` first to see the current content (it may not exist yet).
        2. Explore the repository before writing, so the instructions are concrete and correct: call `list_repository_files`
           on the root and on the relevant folders, and `read_repository_file` on files such as the README, build files
           (`*.sln`/`*.slnx`, `*.csproj`, `package.json`, `pyproject.toml`, `Makefile`, ...), CI workflows in `.github/workflows`,
           linter or formatter settings (`.editorconfig`, ...) and a few representative source and test files.
           Only read what you need; don't read every file.
           When GitHub tools are available (commits, issues, pull requests, workflow runs, code search, ...), they are
           already scoped to this repository: don't pass an owner or repo. Use them when the user asks about the
           repository's history or activity, or to find conventions and recurring problems (for example review comments
           on pull requests or failing workflow runs) that should go into the instructions.
        3. If something important can't be derived from the repository, ask at most 3 short, numbered follow-up questions at a
           time (for example: conventions that aren't visible in the code, or what Copilot keeps getting wrong). Don't ask about
           things you can find out or reasonably decide yourself.
        4. Call `update_instructions_file` with the complete new file content. It replaces the editor content, so when the
           file already has content, keep the parts that are still correct and the user's own rules, and change only what
           should change. Refine it with later calls when the user gives more details.
        5. After updating, briefly summarize what you wrote or changed and what could still be refined, then remind the user
           to review the editor and click "Save & commit".

        Guidelines for a good instructions file:
        - Start with a `#` title, then `##` sections such as: build and test (exact commands, also to run a single test),
          architecture (projects or modules, how they depend on each other, where things live), conventions (coding style,
          naming, patterns to follow and to avoid) and repository guidance (docs that matter, things that are easy to miss).
        - Write short, specific, actionable bullet points in English. Prefer facts Copilot can't infer quickly from the code
          (why things are the way they are, non-obvious rules, pitfalls) over generic advice like "write clean code".
        - Only state what you verified in the repository or what the user told you. Never invent commands, paths or tools.
        - Keep it concise: Copilot code review only reads the first {CopilotInstructionsFile.CodeReviewLimit} characters,
          so put the most important rules first and aim for well under ~{CopilotInstructionsFile.CodeReviewLimit * 2} characters overall.
        - Don't include secrets, personal settings or task-specific instructions; this file applies to every request.
        """;

    public AssistantTools AsTools()
    {
        List<AITool> tools =
        [
            AIFunctionFactory.Create(GetFileAsync, "get_instructions_file"),
            AIFunctionFactory.Create(UpdateFileAsync, "update_instructions_file"),
            AIFunctionFactory.Create(ListRepositoryFilesAsync, "list_repository_files"),
            AIFunctionFactory.Create(ReadRepositoryFileAsync, "read_repository_file")
        ];
        var names = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        tools.AddRange((gitHubTools ?? []).Where(t => names.Add(t.Name)));
        return new(tools, async () => await GetFileAsync());
    }

    [Description("Gets the current content of the Copilot instructions file in the editor, and whether the file already exists in the repository.")]
    public async Task<CopilotInstructionsFormSnapshot> GetFileAsync()
    {
        CopilotInstructionsFormSnapshot? form = null;
        await onUi(() =>
        {
            form = getForm();
        });
        return form!;
    }

    [Description("Replaces the content of the Copilot instructions file in the editor with the given Markdown. The user still reviews and saves it.")]
    public async Task<string> UpdateFileAsync(
        [Description("The complete Markdown content of the instructions file. Replaces the current content.")] string content)
    {
        var changed = new List<string>();
        var problems = new List<string>();
        var markdown = (content ?? string.Empty).Replace("\r\n", "\n").Trim();
        if (markdown.Length == 0)
        {
            problems.Add("The content is empty; nothing was changed.");
            return AgentFormTools.Result(changed, problems);
        }
        if (markdown.Length > MaxContentLength)
        {
            problems.Add($"The content is longer than the maximum of {MaxContentLength} characters; nothing was changed. Shorten it.");
            return AgentFormTools.Result(changed, problems);
        }

        await onUi(() =>
        {
            setContent(markdown + "\n");
        });
        changed.Add("instructions file");
        if (markdown.Length > CopilotInstructionsFile.CodeReviewLimit)
        {
            problems.Add($"Note: the file is {markdown.Length} characters; Copilot code review only reads the first {CopilotInstructionsFile.CodeReviewLimit}.");
        }
        return AgentFormTools.Result(changed, problems);
    }

    [Description("Lists the files and folders of a directory in the selected repository (default branch).")]
    public async Task<string> ListRepositoryFilesAsync(
        [Description("The directory path relative to the repository root, e.g. 'src' or '.github/workflows'. Empty for the root.")] string? path = null,
        CancellationToken cancellationToken = default)
    {
        return await _files.ListRepositoryFilesAsync(path, cancellationToken);
    }

    [Description("Reads a text file of the selected repository (default branch). Long files are cut off.")]
    public async Task<string> ReadRepositoryFileAsync(
        [Description("The file path relative to the repository root, e.g. 'README.md' or 'src/App/App.csproj'.")] string path,
        CancellationToken cancellationToken = default)
    {
        return await _files.ReadRepositoryFileAsync(path, cancellationToken);
    }

    internal const string OutsideRepository = RepositoryFileTools.OutsideRepository;

    /// <summary>Normalizes a repository path; <c>false</c> for paths with '..', which could reach other GitHub API endpoints.</summary>
    internal static bool TryNormalizePath(string? path, out string normalized)
    {
        return RepositoryFileTools.TryNormalizePath(path, out normalized);
    }
}
