using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

public sealed class SearchKnowledgeBaseValidator : AbstractValidator<SearchKnowledgeBaseQuery>
{
    public const int MaxQueryLength = 1_000;

    public SearchKnowledgeBaseValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Query).NotEmpty().MaximumLength(MaxQueryLength);
    }
}
