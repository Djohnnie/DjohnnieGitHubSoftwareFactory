using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItemTemplates;

public sealed class GetWorkItemTemplateValidator : AbstractValidator<GetWorkItemTemplateQuery>
{
    public GetWorkItemTemplateValidator()
    {
        RuleFor(request => request.Dto.Id).NotEmpty();
    }
}
