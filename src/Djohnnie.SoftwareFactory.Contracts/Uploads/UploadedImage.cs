namespace Djohnnie.SoftwareFactory.Contracts.Uploads;

/// <summary>An uploaded image, publicly readable at <paramref name="Url"/>.</summary>
public sealed record UploadedImage(string Url);
