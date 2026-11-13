using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

public sealed class LinkMindmapIdeaValidator : AbstractValidator<LinkMindmapIdeaCommand>
{
    public LinkMindmapIdeaValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.MindmapId).NotEmpty();
        RuleFor(request => request.Dto.IdeaId).NotEmpty();
        RuleFor(request => request.Dto.WorkItemId).NotEqual(Guid.Empty);
    }
}
