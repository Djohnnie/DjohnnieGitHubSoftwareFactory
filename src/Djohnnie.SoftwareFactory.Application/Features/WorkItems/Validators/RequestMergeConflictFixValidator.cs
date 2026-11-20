using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class RequestMergeConflictFixValidator : AbstractValidator<RequestMergeConflictFixCommand>
{
    public RequestMergeConflictFixValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Id).NotEmpty();
    }
}
