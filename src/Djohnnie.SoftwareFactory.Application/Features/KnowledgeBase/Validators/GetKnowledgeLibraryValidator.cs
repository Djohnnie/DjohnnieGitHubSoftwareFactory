using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

public sealed class GetKnowledgeLibraryValidator : AbstractValidator<GetKnowledgeLibraryQuery>
{
    public GetKnowledgeLibraryValidator()
    {
        When(request => request.Dto.RepositoryFullName is not null, () =>
        {
            RuleFor(request => request.Dto.RepositoryFullName!).ValidRepositoryFullName();
        });
    }
}
