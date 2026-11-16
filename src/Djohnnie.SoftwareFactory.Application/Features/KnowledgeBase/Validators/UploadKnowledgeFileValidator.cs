using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

public sealed class UploadKnowledgeFileValidator : AbstractValidator<UploadKnowledgeFileCommand>
{
    public UploadKnowledgeFileValidator(IOptions<KnowledgeBaseOptions> options)
    {
        var settings = options.Value;
        RuleFor(request => request.Dto.Title).KnowledgeFileTitle();
        RuleFor(request => request.Dto.Description).KnowledgeFileDescription();
        RuleFor(request => request.Dto.FolderId).KnowledgeFileFolder(request => request.Dto.RepositoryFullName);
        RuleFor(request => request.Dto.FileName).Must(name => KnowledgeFileNames.Clean(name).Length > 0)
            .WithMessage("A file name is required.")
            .Must(name => (name?.Trim().Length ?? 0) <= KnowledgeFile.MaxFileNameLength)
            .WithMessage($"The file name can be at most {KnowledgeFile.MaxFileNameLength} characters.");
        RuleFor(request => request.Dto.ContentType).MaximumLength(KnowledgeFile.MaxContentTypeLength);
        RuleFor(request => request.Dto.Size).GreaterThan(0).WithMessage("The file is empty.")
            .LessThanOrEqualTo(settings.MaxFileSize).WithMessage($"Files can be at most {settings.MaxFileSizeInMegabytes} MB.");
        RuleFor(request => request.Dto.Content).NotNull();
        When(request => request.Dto.RepositoryFullName is not null, () =>
        {
            RuleFor(request => request.Dto.RepositoryFullName!).ValidRepositoryFullName();
        });
    }
}
