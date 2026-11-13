using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Skills;

public sealed class GetRepositorySkillsValidator : AbstractValidator<GetRepositorySkillsQuery>
{
    public GetRepositorySkillsValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
    }
}
