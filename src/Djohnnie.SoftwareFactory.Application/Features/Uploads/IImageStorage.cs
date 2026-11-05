namespace Djohnnie.SoftwareFactory.Application.Features.Uploads;

/// <summary>Stores images and returns their public URL.</summary>
public interface IImageStorage
{
    Task<string> StoreAsync(string name, byte[] content, string contentType, CancellationToken cancellationToken);
}
