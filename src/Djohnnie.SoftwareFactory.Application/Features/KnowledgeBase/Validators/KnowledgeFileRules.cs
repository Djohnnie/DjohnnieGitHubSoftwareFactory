using Djohnnie.SoftwareFactory.Contracts.KnowledgeBase;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

internal static class KnowledgeFileRules
{
    extension<T>(IRuleBuilder<T, string> rule)
    {
        public IRuleBuilderOptions<T, string> KnowledgeFileTitle()
        {
            return rule.Must(title => !string.IsNullOrWhiteSpace(title)).WithMessage("A title is required.")
                .Must(title => (title?.Trim().Length ?? 0) <= KnowledgeFile.MaxTitleLength)
                .WithMessage($"The title can be at most {KnowledgeFile.MaxTitleLength} characters.");
        }

        public IRuleBuilderOptions<T, string> KnowledgeFileDescription()
        {
            return rule.Must(description => (description?.Trim().Length ?? 0) <= KnowledgeFile.MaxDescriptionLength)
                .WithMessage($"The description can be at most {KnowledgeFile.MaxDescriptionLength} characters.");
        }
    }

    extension<T>(IRuleBuilder<T, Guid?> rule)
    {
        /// <summary>No folder (the top of the library) or a real id.</summary>
        public IRuleBuilderOptions<T, Guid?> KnowledgeFolderId()
        {
            return rule.Must(id => id != Guid.Empty).WithMessage("The folder is invalid.");
        }

        /// <summary>Folders belong to a repository, so a global file (no repository) is always at the top of the library.</summary>
        public IRuleBuilderOptions<T, Guid?> KnowledgeFileFolder(Func<T, string?> repositoryFullName)
        {
            return rule.KnowledgeFolderId()
                .Must((request, id) => id is null || repositoryFullName(request) is not null)
                .WithMessage("A global file can't be in a folder; folders belong to a repository.");
        }
    }
}
