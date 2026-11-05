using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.WorkItemTemplates;

public sealed class GetWorkItemTemplatesValidator : AbstractValidator<GetWorkItemTemplatesQuery>
{
    public GetWorkItemTemplatesValidator()
    {
        RuleFor(request => request.Dto.Type).IsInEnum();
    }
}
