using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed class SetModelFavoriteValidator : AbstractValidator<SetModelFavoriteCommand>
{
    public SetModelFavoriteValidator()
    {
        RuleFor(request => request.Dto.Id).NotEmpty();
    }
}
