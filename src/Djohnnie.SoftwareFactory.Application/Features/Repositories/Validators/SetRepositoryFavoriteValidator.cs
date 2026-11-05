using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Repositories;

public sealed class SetRepositoryFavoriteValidator : AbstractValidator<SetRepositoryFavoriteCommand>
{
    public SetRepositoryFavoriteValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
    }
}
