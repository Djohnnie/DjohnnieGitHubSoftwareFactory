using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.AuthSessions;

public sealed class RemoveAuthSessionValidator : AbstractValidator<RemoveAuthSessionCommand>
{
    public RemoveAuthSessionValidator()
    {
        RuleFor(request => request.Dto).NotNull();
        RuleFor(request => request.Dto.Key).ValidAuthSessionKey();
    }
}
