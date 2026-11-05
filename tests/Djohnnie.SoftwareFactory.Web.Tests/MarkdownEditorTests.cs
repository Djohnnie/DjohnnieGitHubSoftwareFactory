using Bunit;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.Uploads;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.Web.Components.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor.Services;

namespace Djohnnie.SoftwareFactory.Web.Tests;

/// <summary>The contract between MarkdownEditor.razor and MarkdownEditor.razor.js (which wraps Toast UI's public API).</summary>
public class MarkdownEditorTests : BunitContext
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];
    private readonly FakeImageStorage _storage = new();
    private readonly BunitJSModuleInterop _module;
    private readonly BunitJSModuleInterop _editor;

    public MarkdownEditorTests()
    {
        Services.AddMudServices();
        Services.AddLogging();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(dbOptions));
        Services.AddSingleton<IImageStorage>(_storage);
        Services.AddApplication();
        Services.Configure<ImageUploadOptions>(options =>
        {
            options.ConnectionString = "UseDevelopmentStorage=true";
            options.MaxImageSizeInMegabytes = 1;
        });

        JSInterop.Mode = JSRuntimeMode.Loose;
        _module = JSInterop.SetupModule("./Components/Shared/MarkdownEditor.razor.js");
        _editor = _module.SetupModule("create", _ => true);
        _editor.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Render_CreatesTheEditorWithTheValueAndOptions()
    {
        Render<MarkdownEditor>(p => p
            .Add(e => e.Value, "# Hello")
            .Add(e => e.Placeholder, "Describe it")
            .Add(e => e.RawMarkdown, true)
            .Add(e => e.AllowImages, true));

        var options = Assert.Single(_module.Invocations["create"]).Arguments[2]!;
        Assert.Equal("# Hello", Property(options, "Value"));
        Assert.Equal("Describe it", Property(options, "Placeholder"));
        Assert.Equal(true, Property(options, "RawMarkdown"));
        Assert.Equal(true, Property(options, "AllowImages"));
        Assert.Equal(1L * 1024 * 1024, Property(options, "MaxImageSize"));
    }

    [Fact]
    public void Render_WithoutAllowImages_DisablesImages()
    {
        Render<MarkdownEditor>(p => p.Add(e => e.Value, "Text"));

        Assert.Equal(false, Property(Assert.Single(_module.Invocations["create"]).Arguments[2]!, "AllowImages"));
    }

    [Fact]
    public async Task EditorChanges_RaiseValueChanged_AndOutsideChangesAreSetInTheEditor()
    {
        string? changed = null;
        var blurred = false;
        var cut = Render<MarkdownEditor>(p => p
            .Add(e => e.Value, "Before")
            .Add(e => e.ValueChanged, value => { changed = value; })
            .Add(e => e.OnBlur, () => { blurred = true; }));

        await cut.InvokeAsync(() => cut.Instance.OnEditorBlur("Typed"));
        Assert.Equal("Typed", changed);
        Assert.True(blurred);
        Assert.Empty(_editor.Invocations["setMarkdown"]);

        cut.Render(p => p.Add(e => e.Value, "Filled in by the Copilot"));
        Assert.Equal("Filled in by the Copilot", Assert.Single(_editor.Invocations["setMarkdown"]).Arguments[0]);
    }

    [Fact]
    public async Task UploadImage_StoresTheImageAndReturnsItsUrl()
    {
        var cut = Render<MarkdownEditor>(p => p.Add(e => e.AllowImages, true));

        var url = await cut.InvokeAsync(() => cut.Instance.UploadImage(new JSStream(Png), "shot.png", Png.Length));

        var stored = Assert.Single(_storage.Stored);
        Assert.Equal($"https://images.test/{stored.Name}", url);
    }

    [Fact]
    public async Task UploadImage_WithoutAllowImages_StoresNothing()
    {
        var cut = Render<MarkdownEditor>();

        var url = await cut.InvokeAsync(() => cut.Instance.UploadImage(new JSStream(Png), "shot.png", Png.Length));

        Assert.Null(url);
        Assert.Empty(_storage.Stored);
    }

    private static object? Property(object value, string name)
    {
        return value.GetType().GetProperty(name)!.GetValue(value);
    }

    private sealed class JSStream(byte[] content) : IJSStreamReference
    {
        public long Length => content.Length;

        public ValueTask<Stream> OpenReadStreamAsync(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<Stream>(new MemoryStream(content));
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeImageStorage : IImageStorage
    {
        public List<(string Name, byte[] Content, string ContentType)> Stored { get; } = [];

        public Task<string> StoreAsync(string name, byte[] content, string contentType, CancellationToken cancellationToken)
        {
            Stored.Add((name, content, contentType));
            return Task.FromResult($"https://images.test/{name}");
        }
    }
}
