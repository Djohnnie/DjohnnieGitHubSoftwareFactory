using System.Reflection;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.Uploads;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class ImageUploadTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];
    private readonly FakeImageStorage _storage = new();
    private readonly ServiceProvider _services;
    private readonly ImageUploadService _service;

    public ImageUploadTests()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _services = new ServiceCollection().AddLogging()
            .AddSingleton<IImageStorage>(_storage)
            .AddApplication()
            .Configure<ImageUploadOptions>(options =>
            {
                options.ConnectionString = "UseDevelopmentStorage=true";
                options.MaxImageSizeInMegabytes = 1;
            })
            .AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(dbOptions))
            .BuildServiceProvider();
        _service = _services.GetRequiredService<ImageUploadService>();
    }

    public void Dispose()
    {
        _services.Dispose();
    }

    [Fact]
    public async Task Upload_StoresUnderRandomNameWithDetectedFormat()
    {
        var image = await _service.UploadImageAsync("My Screenshot (2).jpeg", Png);

        var (name, content, contentType) = Assert.Single(_storage.Stored);
        Assert.Matches(@"^\d{4}/\d{2}/[0-9a-f]{32}-my-screenshot-2\.png$", name);
        Assert.Equal("image/png", contentType);
        Assert.Equal(Png, content);
        Assert.Equal($"https://images.test/{name}", image.Url);
    }

    [Fact]
    public async Task Upload_RejectsNonImages()
    {
        var error = await Assert.ThrowsAsync<ValidationFailedException>(() => _service.UploadImageAsync("evil.svg", "<svg onload=alert(1)>"u8.ToArray()));

        Assert.Contains("Only PNG, JPEG, GIF and WebP", error.Message);
        Assert.Empty(_storage.Stored);
    }

    [Fact]
    public async Task Upload_RejectsImagesOverTheLimit()
    {
        var content = new byte[1024 * 1024 + 1];
        Png.CopyTo(content, 0);

        var error = await Assert.ThrowsAsync<ValidationFailedException>(() => _service.UploadImageAsync("big.png", content));

        Assert.Contains("at most 1 MB", error.Message);
        Assert.Empty(_storage.Stored);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, "image/gif")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    public void Detect_RecognizesSignatures(byte[] content, string contentType)
    {
        Assert.Equal(contentType, ImageFormat.Detect(content)?.ContentType);
    }

    [Fact]
    public void IsEnabled_RequiresConnectionString()
    {
        Assert.False(new ImageUploadOptions().IsConfigured);
        Assert.True(_service.IsEnabled);
    }

    // MarkdownEditor installs its image hook on this private field of the ToastUIEditor package; guard upgrades.
    [Fact]
    public void ToastUIEditor_ExposesItsInteropObject()
    {
        var field = typeof(ToastUI.Editor).GetField("_instance", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.True(typeof(IJSObjectReference).IsAssignableFrom(field.FieldType));
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
