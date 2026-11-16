using Bunit;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.Contracts.GitHub;
using Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.Web.Components.Shared;
using Djohnnie.SoftwareFactory.Web.Repositories;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class KnowledgeBaseTests : BunitContext
{
    private readonly CancellationToken _cancellationToken = Xunit.TestContext.Current.CancellationToken;
    private const string Repo = "owner/repo";
    private const string OtherRepo = "owner/other";
    private readonly FakeKnowledgeFileStorage _storage = new();
    private readonly FakeKnowledgeSearchIndex _index = new();

    public KnowledgeBaseTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddLogging();
        Services.AddDataProtection();
        Services.AddScoped<ProtectedLocalStorage>();
        Services.AddScoped<RepositoryContext>();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(dbOptions));
        Services.AddSingleton<IKnowledgeFileStorage>(_storage);
        Services.AddSingleton<IKnowledgeSearchIndex>(_index);
        Services.AddApplication();
        Services.Configure<KnowledgeBaseOptions>(options =>
        {
            options.ConnectionString = "UseDevelopmentStorage=true";
            options.MaxFileSizeInMegabytes = 1;
        });
        Services.Configure<KnowledgeSearchOptions>(options =>
        {
            options.Endpoint = "https://search.test";
            options.ApiKey = "key";
            options.MaxResults = 2;
        });
    }

    private KnowledgeBaseService Knowledge => Services.GetRequiredService<KnowledgeBaseService>();

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public async Task Upload_StoresTheBlobUnderTheFileIdAndTheMetadataInTheDatabase()
    {
        var folder = await Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = Repo, Name = " Specs " }, _cancellationToken);

        var file = await Upload("api.pdf", "  API design ", folder.Id, contentType: "application/pdf", description: "The REST API.");

        var (id, content, fileName, contentType) = Assert.Single(_storage.Stored);
        Assert.Equal(file.Id, id);
        Assert.Equal("hello"u8.ToArray(), content);
        Assert.Equal(("api.pdf", "application/pdf"), (fileName, contentType));
        Assert.Equal($"https://files.test/{file.Id}", file.Url);
        var library = await Knowledge.GetLibraryAsync(Repo, _cancellationToken);
        var stored = Assert.Single(library.Files);
        Assert.Equal(("API design", "The REST API.", folder.Id, 5L), (stored.Title, stored.Description, stored.FolderId, stored.Size));
        Assert.Equal($"https://files.test/{file.Id}", stored.Url);
        Assert.Equal("Specs", Assert.Single(library.Folders).Name);
    }

    [Fact]
    public async Task Upload_WithoutContentType_UsesOctetStreamAndKeepsOnlyTheFileName()
    {
        var file = await Upload(@"C:\Users\me\notes.bin", "Notes", contentType: "");

        Assert.Equal(("notes.bin", "application/octet-stream"), (file.FileName, file.ContentType));
    }

    [Fact]
    public async Task Upload_RefusesFilesOverTheLimitAndWithoutTitle()
    {
        var tooBig = await Assert.ThrowsAsync<ValidationFailedException>(() => Knowledge.UploadFileAsync(
            new UploadKnowledgeFileRequest(null, "Big", "", "big.zip", "application/zip", 1024 * 1024 + 1, new MemoryStream()), _cancellationToken));
        var untitled = await Assert.ThrowsAsync<ValidationFailedException>(() => Upload("a.txt", " "));

        Assert.Contains("at most 1 MB", tooBig.Message);
        Assert.Contains("A title is required", untitled.Message);
        Assert.Empty(_storage.Stored);
    }

    [Fact]
    public async Task Upload_IntoAMissingFolder_DeletesTheBlobAgain()
    {
        await Assert.ThrowsAsync<DomainRuleException>(() => Upload("a.txt", "A", Guid.NewGuid()));

        var stored = Assert.Single(_storage.Stored);
        Assert.Contains(stored.Id, _storage.Deleted);
        Assert.Empty((await Knowledge.GetLibraryAsync(null, _cancellationToken)).Files);
    }

    [Fact]
    public async Task SaveFile_ChangesTitleDescriptionAndFolderOnly()
    {
        var file = await Upload("a.txt", "A");
        var folder = await Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = Repo, Name = "Docs" }, _cancellationToken);

        var saved = await Knowledge.SaveFileAsync(file with { Title = "B ", Description = "About B", FolderId = folder.Id, RepositoryFullName = Repo, FileName = "evil.html", Size = 1 }, _cancellationToken);

        Assert.Equal(("B", "About B", folder.Id, "a.txt", 5L), (saved.Title, saved.Description, saved.FolderId, saved.FileName, saved.Size));
        Assert.Equal(file.Url, saved.Url);
    }

    [Fact]
    public async Task DeleteFile_RemovesTheRowAndTheBlob()
    {
        var file = await Upload("a.txt", "A");

        await Knowledge.DeleteFileAsync(file.Id, _cancellationToken);

        Assert.Empty((await Knowledge.GetLibraryAsync(null, _cancellationToken)).Files);
        Assert.Equal([file.Id], _storage.Deleted);
    }

    [Fact]
    public async Task Folders_AreUniquePerParentAndCantMoveBelowThemselves()
    {
        var specs = await Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = Repo, Name = "Specs" }, _cancellationToken);
        var api = await Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = Repo, Name = "API", ParentId = specs.Id }, _cancellationToken);
        await Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = Repo, Name = "API" }, _cancellationToken);

        var duplicate = await Assert.ThrowsAsync<DomainRuleException>(() => Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = Repo, Name = "API", ParentId = specs.Id }, _cancellationToken));
        var cycle = await Assert.ThrowsAsync<DomainRuleException>(() => Knowledge.SaveFolderAsync(specs with { ParentId = api.Id }, _cancellationToken));
        var slash = await Assert.ThrowsAsync<ValidationFailedException>(() => Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = Repo, Name = "a/b" }, _cancellationToken));

        Assert.Contains("already is a folder named 'API'", duplicate.Message);
        Assert.Contains("into itself", cycle.Message);
        Assert.Contains("slashes", slash.Message);
        var library = await Knowledge.GetLibraryAsync(Repo, _cancellationToken);
        Assert.Equal("Specs / API", library.PathName(api.Id));
        Assert.Equal(new HashSet<Guid> { specs.Id, api.Id }, library.SubtreeOf(specs.Id));
    }

    [Fact]
    public async Task DeleteFolder_RefusesFoldersThatArentEmpty()
    {
        var specs = await Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = Repo, Name = "Specs" }, _cancellationToken);
        var file = await Upload("a.txt", "A", specs.Id);

        var error = await Assert.ThrowsAsync<DomainRuleException>(() => Knowledge.DeleteFolderAsync(Repo, specs.Id, _cancellationToken));
        await Knowledge.DeleteFileAsync(file.Id, _cancellationToken);
        await Knowledge.DeleteFolderAsync(Repo, specs.Id, _cancellationToken);

        Assert.Contains("isn't empty", error.Message);
        Assert.Empty((await Knowledge.GetLibraryAsync(Repo, _cancellationToken)).Folders);
    }

    [Fact]
    public async Task Upload_PutsTitleDescriptionAndVirtualPathInTheBlobMetadata()
    {
        var specs = await Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = Repo, Name = "Specs" }, _cancellationToken);
        var api = await Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = Repo, Name = "API", ParentId = specs.Id }, _cancellationToken);

        var nested = await Upload("api.pdf", " API design ", api.Id, description: " The REST API. ");
        var top = await Upload("readme.md", "Read me");

        Assert.Equal(new KnowledgeFileMetadata("API design", "The REST API.", "/Specs/API", Repo), _storage.Metadata[nested.Id]);
        Assert.Equal(new KnowledgeFileMetadata("Read me", "", "/"), _storage.Metadata[top.Id]);
    }

    [Fact]
    public async Task SaveFile_UpdatesTheBlobMetadata()
    {
        var file = await Upload("a.txt", "A");
        var docs = await Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = Repo, Name = "Docs" }, _cancellationToken);

        await Knowledge.SaveFileAsync(file with { Title = "B", Description = "About B", FolderId = docs.Id, RepositoryFullName = Repo }, _cancellationToken);

        Assert.Equal(new KnowledgeFileMetadata("B", "About B", "/Docs", Repo), _storage.Metadata[file.Id]);
    }

    [Fact]
    public async Task SaveFolder_RenamingOrMoving_UpdatesThePathOfEveryFileBelowIt()
    {
        var specs = await Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = Repo, Name = "Specs" }, _cancellationToken);
        var api = await Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = Repo, Name = "API", ParentId = specs.Id }, _cancellationToken);
        var archive = await Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = Repo, Name = "Archive" }, _cancellationToken);
        var inSpecs = await Upload("a.txt", "A", specs.Id);
        var inApi = await Upload("b.txt", "B", api.Id);
        var elsewhere = await Upload("c.txt", "C", archive.Id);

        await Knowledge.SaveFolderAsync(specs with { Name = "Specifications" }, _cancellationToken);
        Assert.Equal("/Specifications", _storage.Metadata[inSpecs.Id].Path);
        Assert.Equal("/Specifications/API", _storage.Metadata[inApi.Id].Path);

        await Knowledge.SaveFolderAsync(api with { ParentId = archive.Id }, _cancellationToken);
        Assert.Equal("/Specifications", _storage.Metadata[inSpecs.Id].Path);
        Assert.Equal("/Archive/API", _storage.Metadata[inApi.Id].Path);
        Assert.Equal("/Archive", _storage.Metadata[elsewhere.Id].Path);
    }

    [Fact]
    public async Task SaveFile_WhenTheMetadataCantBeUpdated_StillSavesTheFile()
    {
        var file = await Upload("a.txt", "A");
        _storage.FailMetadataUpdates = true;

        var saved = await Knowledge.SaveFileAsync(file with { Title = "B" }, _cancellationToken);

        Assert.Equal("B", saved.Title);
        Assert.Equal("B", Assert.Single((await Knowledge.GetLibraryAsync(null, _cancellationToken)).Files).Title);
        Assert.Equal("A", _storage.Metadata[file.Id].Title);
    }

    [Fact]
    public void BlobMetadata_IsPercentEncodedAsciiAndLeavesOutAnEmptyDescription()
    {
        var metadata = AzureBlobKnowledgeFileStorage.ToBlobMetadata(new KnowledgeFileMetadata("Café & co", "", "/Specs/Één"));

        Assert.Equal(new Dictionary<string, string> { ["title"] = "Caf%C3%A9%20%26%20co", ["path"] = "%2FSpecs%2F%C3%89%C3%A9n", ["scope"] = "global" }, metadata);
        Assert.Equal("Café & co", Uri.UnescapeDataString(metadata["title"]));
    }

    [Fact]
    public void BlobMetadata_ShortensValuesToStayWithinAzuresLimit()
    {
        var description = string.Concat(Enumerable.Repeat("é😀", KnowledgeFile.MaxDescriptionLength / 3));
        var title = new string('ü', KnowledgeFile.MaxTitleLength);
        var path = "/" + string.Join("/", Enumerable.Repeat(new string('ø', KnowledgeFolder.MaxNameLength), 20));

        var metadata = AzureBlobKnowledgeFileStorage.ToBlobMetadata(new KnowledgeFileMetadata(title, description, path));

        Assert.All(metadata.Values, value => Assert.True(value.All(char.IsAscii)));
        Assert.True(metadata.Sum(pair => pair.Key.Length + pair.Value.Length) < 8 * 1024);
        Assert.Equal(title, Uri.UnescapeDataString(metadata["title"]));
        var shortened = Uri.UnescapeDataString(metadata["description"]);
        Assert.EndsWith("…", shortened);
        Assert.StartsWith(shortened[..^1], description);
        Assert.True(metadata["description"].Length <= AzureBlobKnowledgeFileStorage.MaxEncodedDescriptionLength);
        Assert.True(metadata["path"].Length <= AzureBlobKnowledgeFileStorage.MaxEncodedPathLength);
    }

    [Fact]
    public async Task Library_ShowsTheGlobalFilesAndOnlyTheSelectedRepositorysFiles()
    {
        await Upload("global.md", "Global");
        await Upload("mine.md", "Mine", repository: "Owner/Repo");
        await Upload("theirs.md", "Theirs", repository: OtherRepo);

        var mine = await Knowledge.GetLibraryAsync(Repo, _cancellationToken);
        var none = await Knowledge.GetLibraryAsync(null, _cancellationToken);

        Assert.Equal(["Global", "Mine"], mine.Files.Select(file => file.Title));
        Assert.Equal([null, Repo], mine.Files.Select(file => file.RepositoryFullName));
        Assert.Equal(["Global"], none.Files.Select(file => file.Title));
    }

    [Fact]
    public async Task RepositoryFiles_HaveTheirScopeAndRepositoryInTheBlobMetadata()
    {
        var global = await Upload("global.md", "Global");
        var mine = await Upload("mine.md", "Mine", repository: Repo);

        Assert.Equal(new KnowledgeFileMetadata("Global", "", "/"), _storage.Metadata[global.Id]);
        Assert.Equal(new KnowledgeFileMetadata("Mine", "", "/", Repo), _storage.Metadata[mine.Id]);

        await Knowledge.SaveFileAsync(mine with { RepositoryFullName = null }, _cancellationToken);
        await Knowledge.SaveFileAsync(global with { RepositoryFullName = Repo }, _cancellationToken);

        Assert.Null(_storage.Metadata[mine.Id].Repository);
        Assert.Equal(Repo, _storage.Metadata[global.Id].Repository);
        var library = await Knowledge.GetLibraryAsync(null, _cancellationToken);
        Assert.Equal(["Mine"], library.Files.Select(file => file.Title));
    }

    [Fact]
    public void BlobMetadata_HasTheScopeAndTheUnencodedRepository()
    {
        var global = AzureBlobKnowledgeFileStorage.ToBlobMetadata(new KnowledgeFileMetadata("A", "", "/"));
        var mine = AzureBlobKnowledgeFileStorage.ToBlobMetadata(new KnowledgeFileMetadata("A", "", "/", "owner/my.repo_1"));

        Assert.Equal("global", global["scope"]);
        Assert.False(global.ContainsKey("repository"));
        Assert.Equal(("repository", "owner/my.repo_1"), (mine["scope"], mine["repository"]));
    }

    [Fact]
    public async Task Upload_RefusesAnInvalidRepositoryName()
    {
        var error = await Assert.ThrowsAsync<ValidationFailedException>(() => Upload("a.txt", "A", repository: "owner/repo' or 1 eq 1"));

        Assert.Contains("Repository Full Name", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_storage.Stored);
    }

    [Fact]
    public async Task Search_ReturnsTheCurrentDetailsOfVisibleFilesOnly()
    {
        var specs = await Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = Repo, Name = "Specs" }, _cancellationToken);
        var global = await Upload("guide.md", "Guidelines");
        var mine = await Upload("api.md", "API design", specs.Id);
        var theirs = await Upload("other.md", "Other", repository: OtherRepo);
        var deleted = await Upload("old.md", "Old", repository: Repo);
        await Knowledge.DeleteFileAsync(deleted.Id, _cancellationToken);
        // The index lags behind: it still has passages of the deleted file and of the other repository's file.
        _index.Hits.AddRange([
            new KnowledgeSearchHit(deleted.Id, "stale"),
            new KnowledgeSearchHit(theirs.Id, "not this repository"),
            new KnowledgeSearchHit(mine.Id, "Use REST."),
            new KnowledgeSearchHit(global.Id, "Log every error."),
            new KnowledgeSearchHit(mine.Id, "a third passage")
        ]);

        var results = await Knowledge.SearchAsync(Repo, "  error handling ", _cancellationToken);

        Assert.Equal(("error handling", Repo, 4), Assert.Single(_index.Searches));
        Assert.Equal([
            new KnowledgeSearchResult(mine.Id, "API design", $"https://files.test/{mine.Id}", "/Specs", Repo, "Use REST."),
            new KnowledgeSearchResult(global.Id, "Guidelines", $"https://files.test/{global.Id}", "/", null, "Log every error.")
        ], results);
    }

    [Fact]
    public void SearchFilter_KeepsTheGlobalPassagesAndTheRepositorysOnly()
    {
        Assert.Equal("scope eq 'global' or scope eq null or repository eq 'owner/repo'", AzureAiKnowledgeSearchIndex.Filter("Owner/Repo"));
        Assert.Equal("scope eq 'global' or scope eq null or repository eq 'it''s'", AzureAiKnowledgeSearchIndex.Filter("it's"));

        var options = AzureAiKnowledgeSearchIndex.BuildOptions("q", "owner/repo", 3, "default");
        var vector = Assert.IsType<Azure.Search.Documents.Models.VectorizableTextQuery>(Assert.Single(options.VectorSearch.Queries));
        Assert.Equal(("q", 3, "text_vector"), (vector.Text, vector.KNearestNeighborsCount, Assert.Single(vector.Fields)));
        Assert.Equal(["file_id", "chunk"], options.Select);
        Assert.Equal("default", options.SemanticSearch.SemanticConfigurationName);
    }

    [Fact]
    public async Task Page_GroupsTheGlobalFilesAboveTheRepositorysFoldersAndFiles()
    {
        await SelectRepositoryAsync();
        await Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = Repo, Name = "Specs" }, _cancellationToken);
        await Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = OtherRepo, Name = "Their folder" }, _cancellationToken);
        await Upload("mine.md", "Mine", repository: Repo);
        await Upload("global.md", "Global file");
        await Upload("theirs.md", "Theirs", repository: OtherRepo);
        Render<MudPopoverProvider>();

        var cut = Render<Components.Pages.KnowledgeBase>();

        cut.WaitForAssertion(() => Assert.Equal(5, cut.FindAll("tbody tr").Count));
        var rows = cut.FindAll("tbody tr").Select(row => row.TextContent).ToList();
        Assert.Contains("Available in every repository", rows[0]);
        Assert.Contains("Global file", rows[1]);
        Assert.Contains(Repo, rows[2]);
        Assert.Contains("Only in this repository · 1 folder, 1 file", rows[2]);
        Assert.Contains("Specs", rows[3]);
        Assert.Contains("Mine", rows[4]);
        Assert.DoesNotContain("Theirs", cut.Markup);
        Assert.DoesNotContain("Their folder", cut.Markup);
        Assert.Contains("New folder", cut.Markup);
    }

    [Fact]
    public async Task Page_WithoutARepository_ShowsOnlyGlobalFilesAndNoFolders()
    {
        await Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = Repo, Name = "Specs" }, _cancellationToken);
        await Upload("global.md", "Global file");
        await Upload("mine.md", "Mine", repository: Repo);
        Render<MudPopoverProvider>();

        var cut = Render<Components.Pages.KnowledgeBase>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count));
        Assert.Contains("Global file", cut.FindAll("tbody tr")[1].TextContent);
        Assert.DoesNotContain("Specs", cut.Markup);
        Assert.DoesNotContain("New folder", cut.Markup);
    }

    [Fact]
    public async Task GlobalFiles_CantBeInAFolder()
    {
        var specs = await Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = Repo, Name = "Specs" }, _cancellationToken);
        var mine = await Upload("mine.md", "Mine", specs.Id);

        var upload = await Assert.ThrowsAsync<ValidationFailedException>(() => Knowledge.UploadFileAsync(
            new UploadKnowledgeFileRequest(specs.Id, "Global", "", "g.md", "text/markdown", 1, new MemoryStream([1])), _cancellationToken));
        var save = await Assert.ThrowsAsync<ValidationFailedException>(() => Knowledge.SaveFileAsync(mine with { RepositoryFullName = null }, _cancellationToken));

        Assert.Contains("A global file can't be in a folder", upload.Message);
        Assert.Contains("A global file can't be in a folder", save.Message);
        Assert.Equal([mine.Id], _storage.Stored.Select(stored => stored.Id));
    }

    [Fact]
    public async Task Folders_BelongToOneRepository()
    {
        var specs = await Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = Repo, Name = "Specs" }, _cancellationToken);
        // The same name is fine in another repository.
        var theirs = await Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = OtherRepo, Name = "Specs" }, _cancellationToken);

        var otherFile = await Assert.ThrowsAsync<DomainRuleException>(() => Upload("a.md", "A", specs.Id, repository: OtherRepo));
        var otherParent = await Assert.ThrowsAsync<DomainRuleException>(() =>
            Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = OtherRepo, Name = "API", ParentId = specs.Id }, _cancellationToken));
        var moved = await Assert.ThrowsAsync<DomainRuleException>(() => Knowledge.SaveFolderAsync(specs with { RepositoryFullName = OtherRepo }, _cancellationToken));
        var noRepository = await Assert.ThrowsAsync<ValidationFailedException>(() => Knowledge.SaveFolderAsync(new KnowledgeFolder { Name = "Loose" }, _cancellationToken));
        await Knowledge.DeleteFolderAsync(OtherRepo, specs.Id, _cancellationToken);

        Assert.Contains("no longer exists in this repository", otherFile.Message);
        Assert.Contains("parent folder no longer exists", otherParent.Message);
        Assert.Contains("no longer exists", moved.Message);
        Assert.Contains("Repository Full Name", noRepository.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([specs.Id], (await Knowledge.GetLibraryAsync(Repo, _cancellationToken)).Folders.Select(folder => folder.Id));
        Assert.Equal([theirs.Id], (await Knowledge.GetLibraryAsync(OtherRepo, _cancellationToken)).Folders.Select(folder => folder.Id));
        Assert.Empty((await Knowledge.GetLibraryAsync(null, _cancellationToken)).Folders);
    }

    [Theory]
    [InlineData("report.pdf", "application/pdf", "inline; filename=\"report.pdf\"; filename*=utf-8''report.pdf")]
    [InlineData("page.html", "text/html", "attachment; filename=\"page.html\"; filename*=utf-8''page.html")]
    [InlineData("logo.svg", "image/svg+xml", "attachment; filename=\"logo.svg\"; filename*=utf-8''logo.svg")]
    [InlineData("café.txt", "text/plain", "inline; filename=\"caf_.txt\"; filename*=utf-8''caf%C3%A9.txt")]
    public void ContentDisposition_ShowsSafeTypesAndKeepsTheFileName(string fileName, string contentType, string expected)
    {
        Assert.Equal(expected, AzureBlobKnowledgeFileStorage.ContentDisposition(fileName, contentType));
    }

    [Theory]
    [InlineData(@"C:\Users\me\notes.bin", "notes.bin")]
    [InlineData("/home/me/notes.bin", "notes.bin")]
    [InlineData(@"docs/old\ notes.bin ", "notes.bin")]
    [InlineData("notes.bin", "notes.bin")]
    [InlineData(@"C:\Users\me\", "")]
    public void FileNames_LoseTheirDirectoryOnEveryOperatingSystem(string fileName, string expected)
    {
        Assert.Equal(expected, KnowledgeFileNames.Clean(fileName));
    }

    [Fact]
    public void BlobName_IsTheFileId()
    {
        var id = Guid.NewGuid();

        Assert.Equal(id.ToString(), AzureBlobKnowledgeFileStorage.BlobName(id));
    }

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(20 * 1024 * 1024, "20 MB")]
    public void FormatSize_UsesReadableUnits(long bytes, string expected)
    {
        Assert.Equal(expected, KnowledgeFile.FormatSize(bytes));
    }

    [Fact]
    public void LinkText_ReplacesSquareBrackets()
    {
        Assert.Equal("Spec (draft)", MarkdownEditor.LinkText("Spec [draft]"));
    }

    [Fact]
    public async Task Page_ShowsTheFoldersAndFilesOfTheOpenedFolder()
    {
        await SelectRepositoryAsync();
        var specs = await Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = Repo, Name = "Specs" }, _cancellationToken);
        await Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = Repo, Name = "API", ParentId = specs.Id }, _cancellationToken);
        var design = await Upload("design.pdf", "Design", specs.Id, contentType: "application/pdf");
        await Upload("readme.md", "Read me");
        Render<MudPopoverProvider>();
        Navigation.NavigateTo(KnowledgeBaseRoutes.FolderPath(specs.Id));

        var cut = Render<Components.Pages.KnowledgeBase>();

        // Inside a folder there are only the repository's folders and files; the global files are at the top of the library.
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("tbody tr").Count));
        var rows = cut.FindAll("tbody tr");
        Assert.Contains(Repo, rows[0].TextContent);
        Assert.Contains("API", rows[1].TextContent);
        Assert.Contains("Empty", rows[1].TextContent);
        Assert.Contains("Design", rows[2].TextContent);
        Assert.Equal(design.Url, rows[2].QuerySelector("a[target='_blank']")!.GetAttribute("href"));
        Assert.DoesNotContain("Read me", cut.Markup);
        Assert.Contains("Knowledge base", cut.Find(".mud-breadcrumbs").TextContent);
        Assert.Contains(KnowledgeBaseRoutes.PublicNotice, cut.Find(".knowledge-public-notice").TextContent);
    }

    [Fact]
    public async Task Page_SearchLooksThroughEveryFolder()
    {
        await SelectRepositoryAsync();
        var specs = await Knowledge.SaveFolderAsync(new KnowledgeFolder { RepositoryFullName = Repo, Name = "Specs" }, _cancellationToken);
        await Upload("design.pdf", "Design", specs.Id, description: "Login flow");
        await Upload("readme.md", "Read me");
        Render<MudPopoverProvider>();

        var cut = Render<Components.Pages.KnowledgeBase>();
        // Global header, Read me, repository header, Specs.
        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll("tbody tr").Count));
        cut.Find(".knowledge-search input").Input("login");

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count));
        Assert.Contains("in Specs", cut.FindAll("tbody tr")[1].TextContent);
    }

    /// <summary>Uploads a global file, or a file of <paramref name="repository"/>; a file in a folder belongs to <see cref="Repo"/> unless another repository is given.</summary>
    private Task<KnowledgeFile> Upload(string fileName, string title, Guid? folderId = null, string contentType = "text/plain", string description = "",
        string? repository = null)
    {
        var content = "hello"u8.ToArray();
        return Knowledge.UploadFileAsync(new UploadKnowledgeFileRequest(folderId, title, description, fileName, contentType, content.Length, new MemoryStream(content),
            repository ?? (folderId is null ? null : Repo)), _cancellationToken);
    }

    private async Task SelectRepositoryAsync()
    {
        await Services.GetRequiredService<RepositoryContext>().SelectAsync(new GitHubRepository(
            1, "repo", Repo, new GitHubOwner("owner", null), $"https://github.com/{Repo}", null, false, false, false, null, "main", null));
    }

    private sealed class FakeKnowledgeSearchIndex : IKnowledgeSearchIndex
    {
        public List<KnowledgeSearchHit> Hits { get; } = [];

        public List<(string Query, string Repository, int Size)> Searches { get; } = [];

        public bool IsConfigured => true;

        public Task<IReadOnlyList<KnowledgeSearchHit>> SearchAsync(string query, string repositoryFullName, int size, CancellationToken cancellationToken)
        {
            Searches.Add((query, repositoryFullName, size));
            return Task.FromResult<IReadOnlyList<KnowledgeSearchHit>>(Hits.Take(size).ToList());
        }
    }

    private sealed class FakeKnowledgeFileStorage : IKnowledgeFileStorage
    {
        public List<(Guid Id, byte[] Content, string FileName, string ContentType)> Stored { get; } = [];

        public List<Guid> Deleted { get; } = [];

        /// <summary>The current metadata per blob.</summary>
        public Dictionary<Guid, KnowledgeFileMetadata> Metadata { get; } = [];

        /// <summary>Makes <see cref="SetMetadataAsync"/> fail, like an unreachable storage account.</summary>
        public bool FailMetadataUpdates { get; set; }

        public bool IsConfigured => true;

        public async Task StoreAsync(Guid id, Stream content, string fileName, string contentType, KnowledgeFileMetadata metadata, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            Stored.Add((id, buffer.ToArray(), fileName, contentType));
            Metadata[id] = metadata;
        }

        public Task SetMetadataAsync(Guid id, KnowledgeFileMetadata metadata, CancellationToken cancellationToken)
        {
            if (FailMetadataUpdates)
            {
                throw new HttpRequestException("The storage account can't be reached.");
            }

            lock (Metadata)
            {
                Metadata[id] = metadata;
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            Deleted.Add(id);
            return Task.CompletedTask;
        }

        public string GetUrl(Guid id)
        {
            return $"https://files.test/{id}";
        }
    }
}
