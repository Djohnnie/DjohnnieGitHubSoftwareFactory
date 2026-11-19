using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class MergePullRequestValidator : AbstractValidator<MergePullRequestCommand>
{
    public MergePullRequestValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Id).NotEmpty();
        RuleFor(request => request.Dto.Method).IsInEnum();
    }
}
