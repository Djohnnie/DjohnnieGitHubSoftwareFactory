using Djohnnie.SoftwareFactory.Contracts.Epics;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.EpicTemplates;

public sealed class SaveEpicTemplateValidator : AbstractValidator<SaveEpicTemplateCommand>
{
    public SaveEpicTemplateValidator()
    {
        RuleFor(request => request.Dto.Template).NotNull();
        When(request => request.Dto.Template is not null, () =>
        {
            RuleFor(request => request.Dto.Template.Id).GreaterThanOrEqualTo(0);
            RuleFor(request => request.Dto.Template.Name).NotEmpty().MaximumLength(200);
            RuleFor(request => request.Dto.Template.TitleTemplate).NotEmpty().MaximumLength(256);
            RuleFor(request => request.Dto.Template.Description).NotEmpty().MaximumLength(Epic.MaxDescriptionLength);
        });
    }
}

public sealed class GetEpicTemplateValidator : AbstractValidator<GetEpicTemplateQuery>
{
    public GetEpicTemplateValidator() => RuleFor(request => request.Dto.Id).GreaterThan(0);
}

public sealed class DeleteEpicTemplateValidator : AbstractValidator<DeleteEpicTemplateCommand>
{
    public DeleteEpicTemplateValidator() => RuleFor(request => request.Dto.Id).GreaterThan(0);
}
