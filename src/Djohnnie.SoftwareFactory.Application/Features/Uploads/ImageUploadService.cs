using Djohnnie.SoftwareFactory.Contracts.Uploads;
using MediatR;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Application.Features.Uploads;

public sealed class ImageUploadService(ISender mediator, IOptions<ImageUploadOptions> options)
{
    /// <summary>Whether an image storage is configured; otherwise the editors refuse images.</summary>
    public bool IsEnabled => options.Value.IsConfigured;

    public long MaxImageSize => options.Value.MaxImageSize;

    public async Task<UploadedImage> UploadImageAsync(string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        return (await mediator.Send(new UploadImageCommand(new(fileName, content)), cancellationToken)).Dto;
    }
}
