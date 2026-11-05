namespace Djohnnie.SoftwareFactory.Contracts.Uploads;

/// <param name="FileName">The original file name; only used to make the stored name recognizable.</param>
/// <param name="Content">The image bytes; the format is detected from the content, not from the name.</param>
public sealed record UploadImageRequest(string FileName, byte[] Content);
