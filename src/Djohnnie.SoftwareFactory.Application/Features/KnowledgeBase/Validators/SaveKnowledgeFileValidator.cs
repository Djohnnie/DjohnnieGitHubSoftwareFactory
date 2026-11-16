using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

public sealed class SaveKnowledgeFileValidator : AbstractValidator<SaveKnowledgeFileCommand>
{
    public SaveKnowledgeFileValidator()
    {
        RuleFor(request => request.Dto.File).NotNull();
        When(request => request.Dto.File is not null, () =>
        {
            RuleFor(request => request.Dto.File.Id).NotEmpty();
            RuleFor(request => request.Dto.File.Title).KnowledgeFileTitle();
            RuleFor(request => request.Dto.File.Description).KnowledgeFileDescription();
            RuleFor(request => request.Dto.File.FolderId).KnowledgeFileFolder(request => request.Dto.File.RepositoryFullName);
            When(request => request.Dto.File.RepositoryFullName is not null, () =>
            {
                RuleFor(request => request.Dto.File.RepositoryFullName!).ValidRepositoryFullName();
            });
        });
    }
}
