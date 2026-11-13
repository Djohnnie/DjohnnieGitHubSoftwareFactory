using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

public sealed class GenerateMindmapWorkItemValidator : AbstractValidator<GenerateMindmapWorkItemQuery>
{
    public GenerateMindmapWorkItemValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.MindmapId).NotEmpty();
        RuleFor(request => request.Dto.IdeaId).NotEmpty();
        RuleFor(request => request.Dto.Type).IsInEnum();
    }
}
