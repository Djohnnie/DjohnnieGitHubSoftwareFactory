using System.Collections.Concurrent;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Application.Features.RepositoryAnalysis;
using Djohnnie.SoftwareFactory.Application.Features.TodoTasks;
using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.RepositoryAnalysis;
using Djohnnie.SoftwareFactory.Contracts.TodoTasks;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.DataAccess.Sql.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class RepositoryAnalysisTests
{
    private const string Repo = "owner/repo";
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public void TreeFilter_LeavesOutBinariesDependenciesBuildOutputLockFilesGeneratedAndLargeFiles()
    {
        var entries = Tree(
            ("README.md", 100), ("src/App/Program.cs", 2_000), ("src/App/bin/Debug/App.dll", 5_000), ("src/App/obj/project.assets.json", 9_000),
            ("web/node_modules/left-pad/index.js", 10), ("package-lock.json", 50_000), ("docs/logo.png", 3_000),
            ("src/Data/Migrations/20260101_Init.Designer.cs", 4_000), ("src/Data/Migrations/AppDbContextModelSnapshot.cs", 4_000),
            ("wwwroot/site.min.js", 1_000), ("data/huge.json", RepositoryTreeFilter.MaxFileSize + 1));
        entries.Add(new GitHubTreeEntry("src", "tree", null));
        entries.Add(new GitHubTreeEntry("external/lib", "commit", null));

        Assert.Equal(["README.md", "src/App/Program.cs"], RepositoryTreeFilter.Filter(entries, null).Select(e => e.Path));
        Assert.Equal(["src/App/Program.cs"], RepositoryTreeFilter.Filter(entries, "/src/App/").Select(e => e.Path));
        Assert.Empty(RepositoryTreeFilter.Filter(entries, "src/Ap"));
    }

    [Fact]
    public void BacklogOutline_IndentsChildrenUnderTheirParent_WithStatusIssueAndId_AndCutsOffALongBacklog()
    {
        var epic = new WorkItem { Id = Guid.NewGuid(), Type = WorkItemType.Epic, Title = "Webshop", Status = WorkItemStatus.Accepted, IssueNumber = 3 };
        var feature = new WorkItem { Id = Guid.NewGuid(), Type = WorkItemType.Feature, Title = "Cart", ParentId = epic.Id, Status = WorkItemStatus.Done, IssueNumber = 4 };
        var story = new WorkItem { Id = Guid.NewGuid(), Type = WorkItemType.Story, Title = "Add to cart", ParentId = feature.Id, Status = WorkItemStatus.InProgress };
        var bug = new WorkItem { Id = Guid.NewGuid(), Type = WorkItemType.Bug, Title = "Crash\non login" };

        var outline = WorkItemBacklog.Outline([bug, story, feature, epic]).ReplaceLineEndings("\n");

        Assert.Equal(
            $"- Epic [accepted, #3] Webshop (id: {epic.Id})\n" +
            $"  - Feature [done, #4] Cart (id: {feature.Id})\n" +
            $"    - User story [in progress] Add to cart (id: {story.Id})\n" +
            $"- Bug [draft] Crash on login (id: {bug.Id})", outline);
        Assert.Equal("(The backlog is empty.)", WorkItemBacklog.Outline([]));
        Assert.EndsWith("[3 more items are not shown: the backlog is too long.]", WorkItemBacklog.Outline([bug, story, feature, epic], 100));
    }

    [Fact]
    public async Task WorkItemTools_ReadADescriptionById_AndStopAtTheReadLimit()
    {
        var parent = new WorkItemReference(Guid.NewGuid(), WorkItemType.Feature, "Cart", WorkItemStatus.Draft, null, null);
        var story = new WorkItem { Id = Guid.NewGuid(), Type = WorkItemType.Story, Title = "Add to cart", ParentId = parent.Id, Parent = parent, Description = "## Acceptance criteria" };
        var tools = new WorkItemTools((id, _) => Task.FromResult(id == story.Id ? story : null), maxReads: 2);

        var result = await tools.GetWorkItemAsync(story.Id.ToString(), _cancellationToken);

        Assert.Equal($"User story [draft] Add to cart (id: {story.Id})\nPart of: Feature Cart (id: {parent.Id})\n\n## Acceptance criteria", result.ReplaceLineEndings("\n"));
        Assert.Contains("not a work item id", await tools.GetWorkItemAsync("story-1", _cancellationToken));
        Assert.Contains("no work item with id", await tools.GetWorkItemAsync(Guid.NewGuid().ToString(), _cancellationToken));
        Assert.Contains("limit of 2 work items", await tools.GetWorkItemAsync(story.Id.ToString(), _cancellationToken));
    }

    [Fact]
    public void TreeFilter_NormalizesFolders_AndRefusesParentSegments()
    {
        Assert.Null(RepositoryTreeFilter.NormalizePrefix(" / "));
        Assert.Equal("src/Api", RepositoryTreeFilter.NormalizePrefix(@".\src\Api\"));
        Assert.True(RepositoryTreeFilter.IsValidPrefix("src/Api"));
        Assert.True(RepositoryTreeFilter.IsValidPrefix(null));
        Assert.False(RepositoryTreeFilter.IsValidPrefix("src/../../other"));
    }

    [Fact]
    public void Outline_ListsFilesWithSizes_OrFoldersWhenThereAreTooMany()
    {
        var files = RepositoryTreeFilter.Filter(Tree(("README.md", 100), ("src/A.cs", 2_048), ("src/Deep/B.cs", 3 * 1024 * 1024 / 20)), null);

        Assert.Equal("README.md  (100 B)\nsrc/A.cs  (2 KB)\nsrc/Deep/B.cs  (153.6 KB)", RepositoryTreeFilter.Outline(files, 10_000).ReplaceLineEndings("\n"));

        var folders = RepositoryTreeFilter.Outline(files, 20).ReplaceLineEndings("\n").Split('\n');
        Assert.StartsWith("[3 files;", folders[0]);
        Assert.Equal(["src/  (2 files, 155.6 KB)", "src/Deep/  (1 files, 153.6 KB)", "README.md  (100 B)"], folders[1..]);
    }

    [Fact]
    public async Task ExplorerTools_ListFromTheTree_ReadOnlyKnownFiles_AndStopAtTheReadLimit()
    {
        var reads = new List<string>();
        var tools = new RepositoryExplorerTools(["README.md", "src/App/Program.cs", "src/App/Startup.cs"], (path, _) =>
        {
            reads.Add(path);
            return Task.FromResult<string?>($"content of {path}");
        }, maxReads: 2);

        Assert.Equal("src/\nREADME.md", tools.ListFiles(null));
        Assert.Equal("src/App/Program.cs\nsrc/App/Startup.cs", tools.ListFiles("src/App"));
        Assert.Contains("can't contain '..'", tools.ListFiles("../other"));
        Assert.Contains("can't be read", await tools.ReadFileAsync("src/App/bin/App.dll", _cancellationToken));
        Assert.Equal("content of README.md", await tools.ReadFileAsync("./README.md", _cancellationToken));
        Assert.Equal("content of src/App/Program.cs", await tools.ReadFileAsync("src/App/Program.cs", _cancellationToken));
        Assert.Contains("limit of 2 files", await tools.ReadFileAsync("src/App/Startup.cs", _cancellationToken));
        Assert.Equal(["README.md", "src/App/Program.cs"], reads);
    }

    [Fact]
    public async Task StartRun_IsRefused_WhileTheRepositoryIsBeingAnalyzed_AndForInvalidInput()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        await using var services = Services(database, new ScriptedChatClient(), new FakeGitHub());
        var todo = services.GetRequiredService<TodoTaskService>();

        var task = await todo.StartRepositoryAnalysisAsync(new(Repo, RepositoryAnalysisMode.Improvements, "/src/", 5, "octocat"), _cancellationToken);

        Assert.Equal((TodoTaskKind.AnalyzeRepository, TodoTaskStatus.Pending, "octocat"), (task.Kind, task.Status, task.CreatedBy));
        Assert.Equal("Analyze 'src' (improvements)", task.Title);
        await Assert.ThrowsAsync<DomainRuleException>(() => todo.StartRepositoryAnalysisAsync(new(Repo, RepositoryAnalysisMode.DescribeExisting, null, 5, null), _cancellationToken));
        await Assert.ThrowsAsync<ValidationFailedException>(() => todo.StartRepositoryAnalysisAsync(new("owner/other", RepositoryAnalysisMode.Improvements, "../x", 5, null), _cancellationToken));
        await Assert.ThrowsAsync<ValidationFailedException>(() => todo.StartRepositoryAnalysisAsync(new("owner/other", RepositoryAnalysisMode.Improvements, null, 0, null), _cancellationToken));
        await InternalAgentTests.DisableAsync(services, InternalAgentKeys.RepositoryWorkItemWriter);
        await Assert.ThrowsAsync<DomainRuleException>(() => todo.StartRepositoryAnalysisAsync(new("owner/other", RepositoryAnalysisMode.Improvements, null, 5, null), _cancellationToken));
    }

    [Fact]
    public async Task ARun_PlansEpicsAndFeatures_ThenWritesEachFeature_AllAsTaggedDrafts()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var gitHub = new FakeGitHub();
        var chat = new ScriptedChatClient(
            ScriptedChatClient.ToolCall("read_repository_file", new Dictionary<string, object?> { ["path"] = "README.md" }),
            Json(new
            {
                epics = new object[]
                {
                    new
                    {
                        title = "Reliability", description = "## Goal\nFewer crashes.",
                        features = new[]
                        {
                            new { title = "Error handling", summary = "Handle failures in the API.", paths = new[] { "src/App/Program.cs" } },
                            new { title = "Existing feature", summary = "Already planned.", paths = Array.Empty<string>() }
                        }
                    },
                    new { title = "existing epic", description = "Ignored.", features = new[] { new { title = "Docs", summary = "Write docs.", paths = new[] { "README.md" } } } }
                }
            }));
        await using var services = Services(database, chat, gitHub);
        var items = services.GetRequiredService<WorkItemService>();
        var existingEpic = await items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Epic, Title = "Existing epic", Description = "Keep me." }, _cancellationToken);
        await items.SaveAsync(new WorkItem { RepositoryFullName = Repo, Type = WorkItemType.Feature, ParentId = existingEpic.Id, Title = "Existing feature" }, _cancellationToken);
        var todo = services.GetRequiredService<TodoTaskService>();
        var root = await todo.StartRepositoryAnalysisAsync(new(Repo, RepositoryAnalysisMode.Improvements, null, 5, "octocat"), _cancellationToken);

        Assert.Equal(TodoTaskStatus.Waiting, await DispatchAndExecuteAsync(services, root.Id));

        var input = string.Join("\n", chat.Requests[0].Where(m => m.Role == ChatRole.User).Select(m => m.Text));
        Assert.Contains("\"mode\": \"improvements\"", input);
        Assert.Contains("src/App/Program.cs  (2 KB)", input);
        Assert.DoesNotContain("App.dll", input);
        Assert.Contains($"- Epic [draft] Existing epic (id: {existingEpic.Id})", input);
        Assert.Contains("  - Feature [draft] Existing feature (id: ", input);
        Assert.Contains("`get_work_item`", input);
        Assert.Contains(chat.Options[0]!.Tools!, tool => tool.Name == "get_work_item");
        Assert.Contains("You are the Repository Analyst", InternalAgentTests.SystemPrompt(chat, 0));
        Assert.Contains(gitHub.Requests, path => path.EndsWith("/contents/README.md", StringComparison.Ordinal));

        var epics = await items.GetAllAsync(Repo, [WorkItemType.Epic], cancellationToken: _cancellationToken);
        var reliability = Assert.Single(epics, e => e.Title == "Reliability");
        Assert.Equal((root.Id, "## Goal\nFewer crashes.", WorkItemStatus.Generated), (reliability.GeneratedByTaskId!.Value, reliability.Description, reliability.Status));
        Assert.Equal(("Keep me.", WorkItemStatus.Draft), (epics.Single(e => e.Id == existingEpic.Id).Description, epics.Single(e => e.Id == existingEpic.Id).Status));
        var features = await items.GetAllAsync(Repo, [WorkItemType.Feature], cancellationToken: _cancellationToken);
        var errorHandling = Assert.Single(features, f => f.Title == "Error handling");
        Assert.Equal(reliability.Id, errorHandling.ParentId);
        Assert.Contains("- `src/App/Program.cs`", errorHandling.Description);
        Assert.Equal(existingEpic.Id, features.Single(f => f.Title == "Docs").ParentId);
        Assert.Single(features, f => f.Title == "Existing feature");

        var run = await todo.GetRunAsync(Repo, root.Id, _cancellationToken);
        Assert.Equal(2, run.Total);
        Assert.Equal("Planned 2 features in 1 new and 1 existing epics (2 readable files; the analyst read 1).", run.Root.Result);

        // The writer of the first feature: reads an existing epic, describes the feature and adds a user story and a bug.
        chat.Enqueue(ScriptedChatClient.ToolCall("get_work_item", new Dictionary<string, object?> { ["id"] = existingEpic.Id.ToString() }));
        chat.Enqueue(Json(new
        {
            featureDescription = "## Current situation\nExceptions escape `Program.cs`.",
            stories = new[] { new { title = "Return problem details", description = "## Acceptance criteria\n- 500s are JSON." } },
            bugs = new[] { new { title = "Null reference on empty body", description = "Crashes." } }
        }));
        var first = run.SubTasks.Single(t => t.Title.Contains("Error handling"));
        Assert.Equal(TodoTaskStatus.Succeeded, await DispatchAndExecuteAsync(services, first.Id));

        Assert.Contains("You are the Repository Work Item Writer", InternalAgentTests.SystemPrompt(chat, 2));
        var writerInput = string.Join("\n", chat.Requests[2].Where(m => m.Role == ChatRole.User).Select(m => m.Text));
        Assert.Contains("Docs", writerInput);
        Assert.Contains($"\n  - Feature [generated draft] Error handling (id: {errorHandling.Id})", writerInput.ReplaceLineEndings("\n"));
        var epicResult = chat.Requests[3].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single().Result?.ToString();
        Assert.Contains($"Epic [draft] Existing epic (id: {existingEpic.Id})", epicResult);
        Assert.EndsWith("Keep me.", epicResult);
        var described = await items.GetAsync(Repo, errorHandling.Id, _cancellationToken);
        Assert.Equal("## Current situation\nExceptions escape `Program.cs`.", described!.Description);
        var children = await items.GetAllAsync(Repo, parentId: errorHandling.Id, cancellationToken: _cancellationToken);
        Assert.Equal([(WorkItemType.Bug, "Null reference on empty body"), (WorkItemType.Story, "Return problem details")],
            children.Select(c => (c.Type, c.Title)).OrderBy(c => c.Title));
        Assert.All(children, c => Assert.Equal((WorkItemStatus.Generated, root.Id), (c.Status, c.GeneratedByTaskId!.Value)));
        Assert.True(described.IsGenerated && described.IsDraft);

        // The writer of the second feature returns nothing; the run then ends.
        chat.Enqueue(Json(new { featureDescription = "", stories = Array.Empty<object>(), bugs = Array.Empty<object>() }));
        Assert.Equal(TodoTaskStatus.Succeeded, await DispatchAndExecuteAsync(services, run.SubTasks.Single(t => t.Title.Contains("Docs")).Id));
        run = await todo.GetRunAsync(Repo, root.Id, _cancellationToken);
        Assert.Equal(TodoTaskStatus.Succeeded, run.Root.Status);
        Assert.Contains("Kept the feature's description and wrote 0 user stories and 0 bugs", run.SubTasks.Single(t => t.Title.Contains("Docs")).Result);

        var discarded = await todo.DiscardDraftsAsync(Repo, root.Id, _cancellationToken);
        Assert.Equal((5, 0), (discarded.Deleted, discarded.Kept));
        Assert.Equal(["Existing epic", "Existing feature"], (await items.GetAllAsync(Repo, cancellationToken: _cancellationToken)).Select(i => i.Title).Order());

        // OpenAI's strict structured output refuses a $ref that doesn't point to a top-level definition.
        var schemas = chat.Options.Select(o => o?.ResponseFormat).OfType<ChatResponseFormatJson>().Select(f => f.Schema?.GetRawText()).ToList();
        Assert.Equal(chat.Requests.Count, schemas.Count);
        Assert.All(schemas, schema => Assert.DoesNotContain("#/properties", schema));
    }

    [Fact]
    public async Task ARun_FailsWithoutRetries_WhenTheRepositoryHasNoReadableFiles()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var chat = new ScriptedChatClient();
        await using var services = Services(database, chat, new FakeGitHub(("docs/logo.png", 100)));
        var todo = services.GetRequiredService<TodoTaskService>();
        var root = await todo.StartRepositoryAnalysisAsync(new(Repo, RepositoryAnalysisMode.DescribeExisting, null, 5, null), _cancellationToken);

        Assert.Equal(TodoTaskStatus.Failed, await DispatchAndExecuteAsync(services, root.Id));

        var run = await todo.GetRunAsync(Repo, root.Id, _cancellationToken);
        Assert.Equal(("The repository has no files that can be analyzed.", 1), (run.Root.Error, run.Root.Attempts));
        Assert.Empty(chat.Requests);
        await Assert.ThrowsAsync<DomainRuleException>(() => todo.DiscardDraftsAsync(Repo, Guid.NewGuid(), _cancellationToken));

        // A failed run can be deleted; a run that is still active can't.
        var active = await todo.StartRepositoryAnalysisAsync(new("owner/other", RepositoryAnalysisMode.Improvements, null, 5, null), _cancellationToken);
        Assert.Contains("Cancel it first", (await Assert.ThrowsAsync<DomainRuleException>(() => todo.DeleteRunAsync("owner/other", active.Id, _cancellationToken))).Message);
        Assert.Equal("The run no longer exists.", (await Assert.ThrowsAsync<DomainRuleException>(() => todo.DeleteRunAsync(Repo, Guid.NewGuid(), _cancellationToken))).Message);
        await todo.DeleteRunAsync(Repo, root.Id, _cancellationToken);
        Assert.Empty(await todo.GetRunsAsync(Repo, _cancellationToken));
        Assert.Single(await todo.GetRunsAsync("owner/other", _cancellationToken));
    }

    [Fact]
    public async Task WithSnapshotStorage_ARunDownloadsTheRepositoryOnce_AndAllItsTasksReadFromTheSnapshot()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var gitHub = new FakeGitHub();
        var storage = new MemorySnapshotStorage();
        var chat = new ScriptedChatClient(
            ScriptedChatClient.ToolCall("read_repository_file", new Dictionary<string, object?> { ["path"] = "README.md" }),
            Json(new { epics = new[] { new { title = "Reliability", description = "## Goal", features = new[] { new { title = "Error handling", summary = "Handle failures.", paths = new[] { "src/App/Program.cs" } } } } } }),
            ScriptedChatClient.ToolCall("read_repository_file", new Dictionary<string, object?> { ["path"] = "src/App/Program.cs" }),
            Json(new { featureDescription = "## Current situation", stories = new[] { new { title = "Return problem details", description = "## Acceptance criteria" } }, bugs = Array.Empty<object>() }));
        await using var services = Services(database, chat, gitHub, storage);
        var todo = services.GetRequiredService<TodoTaskService>();
        var root = await todo.StartRepositoryAnalysisAsync(new(Repo, RepositoryAnalysisMode.Improvements, null, 5, null), _cancellationToken);

        Assert.Equal(TodoTaskStatus.Waiting, await DispatchAndExecuteAsync(services, root.Id));
        var run = await todo.GetRunAsync(Repo, root.Id, _cancellationToken);
        Assert.Equal(TodoTaskStatus.Succeeded, await DispatchAndExecuteAsync(services, Assert.Single(run.SubTasks).Id));

        Assert.Equal([$"/repos/{Repo}/tarball/HEAD"], gitHub.Requests);
        Assert.Equal(["files/README.md", "files/src/App/Program.cs", "manifest.json"], storage.Names(root.Id));
        var toolResults = chat.Requests.SelectMany(r => r).SelectMany(m => m.Contents).OfType<FunctionResultContent>().Select(r => r.Result?.ToString()).ToList();
        Assert.Equal(["# README.md", "# src/App/Program.cs"], toolResults);
        var analystInput = string.Join("\n", chat.Requests[0].Where(m => m.Role == ChatRole.User).Select(m => m.Text));
        Assert.Contains("src/App/Program.cs  (", analystInput);
        Assert.DoesNotContain("App.dll", analystInput);

        Assert.Equal(TodoTaskStatus.Succeeded, (await todo.GetRunAsync(Repo, root.Id, _cancellationToken)).Root.Status);
        await using var scope = services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<RepositorySnapshotCleaner>().CleanUpAsync(_cancellationToken));
        Assert.Empty(storage.Names(root.Id));
    }

    [Fact]
    public async Task WithSnapshotStorage_ARunFails_WhenTheRepositoryIsLargerThanASnapshotMayBe_AndActiveRunsKeepTheirSnapshot()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var storage = new MemorySnapshotStorage();
        await using var services = Services(database, new ScriptedChatClient(), new FakeGitHub(), storage, options => options.MaxFiles = 1);
        var todo = services.GetRequiredService<TodoTaskService>();
        var root = await todo.StartRepositoryAnalysisAsync(new(Repo, RepositoryAnalysisMode.DescribeExisting, null, 5, null), _cancellationToken);
        var active = Guid.NewGuid();
        var activeRun = await services.GetRequiredService<ITodoTaskStore>().CreateRunAsync("owner/other", new NewTodoTask(TodoTaskKind.AnalyzeRepository, "Other", "{}"), null, 3, _cancellationToken);
        await storage.WriteAsync(activeRun.Id, "manifest.json", [1], _cancellationToken);
        await storage.WriteAsync(active, "manifest.json", [1], _cancellationToken);

        Assert.Equal(TodoTaskStatus.Failed, await DispatchAndExecuteAsync(services, root.Id));

        Assert.Contains("more than 1 readable files", (await todo.GetRunAsync(Repo, root.Id, _cancellationToken)).Root.Error);
        Assert.Empty(storage.Names(root.Id));
        await using var scope = services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<RepositorySnapshotCleaner>().CleanUpAsync(_cancellationToken));
        Assert.NotEmpty(storage.Names(activeRun.Id));
        Assert.Empty(storage.Names(active));
    }

    private async Task<TodoTaskStatus?> DispatchAndExecuteAsync(IServiceProvider services, Guid id)
    {
        // One dispatch queues every due task, so a sub-task may already have been queued by an earlier one.
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TodoTaskDispatcher>().DispatchAsync(_cancellationToken);
        Assert.Equal(TodoTaskStatus.Queued, await scope.ServiceProvider.GetRequiredService<ITodoTaskStore>().GetStatusAsync(id, _cancellationToken));
        return await scope.ServiceProvider.GetRequiredService<TodoTaskExecutor>().ExecuteAsync(id, _cancellationToken);
    }

    private static ServiceProvider Services(SqlServerDatabase database, ScriptedChatClient chat, FakeGitHub gitHub, MemorySnapshotStorage? storage = null,
        Action<RepositorySnapshotOptions>? snapshotOptions = null)
    {
        var client = new GitHubApiClient(new HttpClient(gitHub) { BaseAddress = new Uri("https://api.github.com/") }, new ConfiguredGitHubTokenProvider("token"));
        var services = new ServiceCollection().AddLogging().AddApplication()
            .AddSingleton(database.Factory)
            .AddSingleton(new AssistantAgentFactory(chat, NullLoggerFactory.Instance))
            .AddSingleton(client)
            .Configure(snapshotOptions ?? (_ => { }));
        if (storage is not null)
        {
            services.AddSingleton<IRepositorySnapshotStorage>(storage);
        }
        return services.BuildServiceProvider();
    }

    /// <summary>Snapshot storage in memory: blob names per run.</summary>
    private sealed class MemorySnapshotStorage : IRepositorySnapshotStorage
    {
        private readonly ConcurrentDictionary<(Guid RunId, string Name), byte[]> _blobs = new();

        public bool IsConfigured
        {
            get
            {
                return true;
            }
        }

        public List<string> Names(Guid runId)
        {
            return _blobs.Keys.Where(k => k.RunId == runId).Select(k => k.Name).Order(StringComparer.Ordinal).ToList();
        }

        public Task WriteAsync(Guid runId, string name, byte[] content, CancellationToken cancellationToken)
        {
            _blobs[(runId, name)] = content;
            return Task.CompletedTask;
        }

        public Task<byte[]?> ReadAsync(Guid runId, string name, CancellationToken cancellationToken)
        {
            return Task.FromResult(_blobs.TryGetValue((runId, name), out var content) ? content : null);
        }

        public Task<IReadOnlyList<Guid>> GetRunsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<Guid>>(_blobs.Keys.Select(k => k.RunId).Distinct().ToList());
        }

        public Task DeleteAsync(Guid runId, CancellationToken cancellationToken)
        {
            foreach (var key in _blobs.Keys.Where(k => k.RunId == runId).ToList())
            {
                _blobs.TryRemove(key, out _);
            }
            return Task.CompletedTask;
        }
    }

    private static ChatResponse Json(object value)
    {
        return ScriptedChatClient.Text(JsonSerializer.Serialize(value));
    }

    private static List<GitHubTreeEntry> Tree(params (string Path, long Size)[] files)
    {
        return files.Select(f => new GitHubTreeEntry(f.Path, "blob", f.Size)).ToList();
    }

    /// <summary>Serves the tree of <c>owner/repo</c> and the contents of its files.</summary>
    private sealed class FakeGitHub(params (string Path, long Size)[] files) : HttpMessageHandler
    {
        private readonly (string Path, long Size)[] _files = files.Length > 0
            ? files
            : [("README.md", 100), ("src/App/Program.cs", 2_048), ("src/App/bin/Debug/App.dll", 5_000)];

        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(path);
            if (path == $"/repos/{Repo}/tarball/HEAD")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Tarball()) });
            }
            if (path == $"/repos/{Repo}/git/trees/HEAD")
            {
                var tree = new { sha = "abc", truncated = false, tree = _files.Select(f => new { path = f.Path, type = "blob", size = f.Size }) };
                return Task.FromResult(Respond(HttpStatusCode.OK, JsonSerializer.Serialize(tree)));
            }

            var prefix = $"/repos/{Repo}/contents/";
            if (path.StartsWith(prefix, StringComparison.Ordinal) && _files.Any(f => f.Path == Uri.UnescapeDataString(path[prefix.Length..])))
            {
                var file = Uri.UnescapeDataString(path[prefix.Length..]);
                var body = new { path = file, sha = "sha", type = "file", encoding = "base64", content = Convert.ToBase64String(Encoding.UTF8.GetBytes($"# {file}")) };
                return Task.FromResult(Respond(HttpStatusCode.OK, JsonSerializer.Serialize(body)));
            }
            return Task.FromResult(Respond(HttpStatusCode.NotFound, "{\"message\":\"Not Found\"}"));
        }

        /// <summary>The files as GitHub's archive: gzipped tar, a global header, and everything under one root folder.</summary>
        private byte[] Tarball()
        {
            using var archive = new MemoryStream();
            using (var gzip = new GZipStream(archive, CompressionLevel.Fastest, leaveOpen: true))
            using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
            {
                tar.WriteEntry(new PaxGlobalExtendedAttributesTarEntry(new Dictionary<string, string> { ["comment"] = "abc1234" }));
                tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "owner-repo-abc1234/"));
                foreach (var file in _files)
                {
                    tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, $"owner-repo-abc1234/{file.Path}")
                    {
                        DataStream = new MemoryStream(Encoding.UTF8.GetBytes($"# {file.Path}"))
                    });
                }
            }
            return archive.ToArray();
        }

        private static HttpResponseMessage Respond(HttpStatusCode status, string json)
        {
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
