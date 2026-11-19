using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Mindmaps;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Mindmaps;

public sealed class SaveMindmapPlanValidator : AbstractValidator<SaveMindmapPlanCommand>
{
    public SaveMindmapPlanValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.MindmapId).NotEmpty();
        RuleFor(request => request.Dto.Items).NotEmpty().WithMessage("Select at least one item to save.")
            .Must(items => items.Count <= MindmapPlanItem.MaxPerPlan).WithMessage($"At most {MindmapPlanItem.MaxPerPlan} items can be saved at once.");
        RuleForEach(request => request.Dto.Items).SetValidator(new ContractValidator<MindmapPlanItem>());
        RuleForEach(request => request.Dto.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Id).NotEmpty();
            item.RuleFor(i => i.Type).Must(type => MindmapPlanItem.Types.Contains(type))
                .WithMessage("A mindmap only becomes an epic, features and user stories.");
            item.RuleFor(i => i.IdeaIds).NotNull();
        });
    }
}
