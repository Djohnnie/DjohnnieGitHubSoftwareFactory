using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Agents;

public sealed class SetAgentFavoriteValidator : AbstractValidator<SetAgentFavoriteCommand>
{
    public SetAgentFavoriteValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.FileName).NotEmpty().MaximumLength(100);
    }
}
