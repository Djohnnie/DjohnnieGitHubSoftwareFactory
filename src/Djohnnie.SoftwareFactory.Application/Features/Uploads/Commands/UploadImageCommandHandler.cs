using System.Text.RegularExpressions;
using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Uploads;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Uploads;

public sealed partial class UploadImageCommandHandler(IImageStorage storage, TimeProvider time) : IRequestHandler<UploadImageCommand, Response<UploadedImage>>
{
    public async Task<Response<UploadedImage>> Handle(UploadImageCommand request, CancellationToken cancellationToken)
    {
        // The validator guarantees a known format.
        var format = ImageFormat.Detect(request.Dto.Content)!;
        var now = time.GetUtcNow();

        // A random name keeps the public URL unguessable; it never contains the (possibly private) repository name.
        var name = $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{Slug(request.Dto.FileName)}{format.Extension}";
        var url = await storage.StoreAsync(name, request.Dto.Content, format.ContentType, cancellationToken);
        return new(new UploadedImage(url));
    }

    private static string Slug(string fileName)
    {
        var slug = UnsafeCharacters().Replace(Path.GetFileNameWithoutExtension(fileName ?? string.Empty).ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > 40)
        {
            slug = slug[..40].TrimEnd('-');
        }

        return slug.Length == 0 ? string.Empty : $"-{slug}";
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex UnsafeCharacters();
}
