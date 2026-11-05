using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.AuthSessions;

public sealed class SaveAuthSessionValidator : AbstractValidator<SaveAuthSessionCommand>
{
    public SaveAuthSessionValidator()
    {
        RuleFor(request => request.Dto).NotNull();
        RuleFor(request => request.Dto.Key).ValidAuthSessionKey();
        RuleFor(request => request.Dto.Ticket).NotEmpty();
    }
}
