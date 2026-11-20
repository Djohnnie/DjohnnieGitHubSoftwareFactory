using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItems;

public sealed class FixMergeConflictsValidator : AbstractValidator<FixMergeConflictsCommand>
{
    public FixMergeConflictsValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Id).NotEmpty();
    }
}
