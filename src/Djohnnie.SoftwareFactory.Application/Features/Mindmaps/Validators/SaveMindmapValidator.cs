using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

public sealed class SaveMindmapValidator : AbstractValidator<SaveMindmapCommand>
{
    public SaveMindmapValidator()
    {
        RuleFor(request => request.Dto.Mindmap).NotNull();
        When(request => request.Dto.Mindmap is not null, () =>
        {
            RuleFor(request => request.Dto.Mindmap.RepositoryFullName).ValidRepositoryFullName();
            RuleFor(request => request.Dto.Mindmap.Nodes).NotNull()
                .Must(nodes => nodes is null || nodes.Count <= Mindmap.MaxNodes).WithMessage($"A mindmap has at most {Mindmap.MaxNodes} ideas.");
            RuleForEach(request => request.Dto.Mindmap.Nodes).ChildRules(node =>
            {
                node.RuleFor(n => n.Text).Must(text => !string.IsNullOrWhiteSpace(text)).WithMessage("Every idea needs a text.")
                    .Must(text => text is null || text.Trim().Length <= Mindmap.MaxTextLength)
                    .WithMessage($"An idea has at most {Mindmap.MaxTextLength} characters.");
                node.RuleFor(n => n.X).Must(double.IsFinite).WithMessage("An idea needs a valid position.");
                node.RuleFor(n => n.Y).Must(double.IsFinite).WithMessage("An idea needs a valid position.");
            });
            RuleFor(request => request.Dto.Mindmap).Custom((mindmap, context) =>
            {
                if (mindmap.Nodes is not null && mindmap.StructureProblem() is { } problem)
                {
                    context.AddFailure("Nodes", problem);
                }
            });
        });
    }
}
