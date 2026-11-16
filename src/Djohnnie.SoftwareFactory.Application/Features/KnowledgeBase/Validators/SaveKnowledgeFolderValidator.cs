using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

public sealed class SaveKnowledgeFolderValidator : AbstractValidator<SaveKnowledgeFolderCommand>
{
    public SaveKnowledgeFolderValidator()
    {
        RuleFor(request => request.Dto.Folder).NotNull();
        When(request => request.Dto.Folder is not null, () =>
        {
            // Folders always belong to a repository; only files can be global.
            RuleFor(request => request.Dto.Folder.RepositoryFullName).ValidRepositoryFullName();
            RuleFor(request => request.Dto.Folder.Name).Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("A folder name is required.")
                .Must(name => (name?.Trim().Length ?? 0) <= KnowledgeFolder.MaxNameLength)
                .WithMessage($"A folder name can be at most {KnowledgeFolder.MaxNameLength} characters.")
                .Must(name => name is null || name.IndexOfAny(['/', '\\']) < 0).WithMessage("A folder name can't contain slashes.");
            RuleFor(request => request.Dto.Folder.ParentId).KnowledgeFolderId()
                .Must((request, parentId) => parentId is null || parentId != request.Dto.Folder.Id).WithMessage("A folder can't be moved into itself.");
        });
    }
}
