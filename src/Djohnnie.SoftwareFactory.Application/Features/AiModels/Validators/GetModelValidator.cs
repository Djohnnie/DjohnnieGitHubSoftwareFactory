using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed class GetModelValidator : AbstractValidator<GetModelQuery>
{
    public GetModelValidator()
    {
        RuleFor(request => request.Dto.Id).NotEmpty();
    }
}
