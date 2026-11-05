using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Agents;

public sealed class GetCustomAgentsValidator : AbstractValidator<GetCustomAgentsQuery>
{
    public GetCustomAgentsValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).NotEmpty();
    }
}
