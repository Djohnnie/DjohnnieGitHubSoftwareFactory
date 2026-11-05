using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.AuthSessions;

public sealed class GetAuthSessionValidator : AbstractValidator<GetAuthSessionQuery>
{
    public GetAuthSessionValidator()
    {
        RuleFor(request => request.Dto).NotNull();
        RuleFor(request => request.Dto.Key).ValidAuthSessionKey();
    }
}
