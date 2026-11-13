using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

public sealed class GetLinkedMindmapIdeasValidator : AbstractValidator<GetLinkedMindmapIdeasQuery>
{
    public GetLinkedMindmapIdeasValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Id).NotEmpty();
    }
}
