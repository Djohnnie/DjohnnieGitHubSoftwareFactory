using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Repositories;

public sealed class SetRepositoryArchivedValidator : AbstractValidator<SetRepositoryArchivedCommand>
{
    public SetRepositoryArchivedValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
    }
}
