using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

public sealed class GenerateMindmapPlanValidator : AbstractValidator<GenerateMindmapPlanQuery>
{
    public GenerateMindmapPlanValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.MindmapId).NotEmpty();
    }
}
