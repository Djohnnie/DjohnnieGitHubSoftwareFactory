using FluentValidation;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Application.Features.Uploads;

public sealed class UploadImageValidator : AbstractValidator<UploadImageCommand>
{
    public UploadImageValidator(IOptions<ImageUploadOptions> options)
    {
        var settings = options.Value;
        RuleFor(request => request.Dto.FileName).NotNull();
        RuleFor(request => request.Dto.Content).NotEmpty()
            .Must(content => content.LongLength <= settings.MaxImageSize)
            .WithMessage($"Images can be at most {settings.MaxImageSizeInMegabytes} MB.")
            .Must(content => ImageFormat.Detect(content) is not null)
            .WithMessage("Only PNG, JPEG, GIF and WebP images can be uploaded.");
    }
}
