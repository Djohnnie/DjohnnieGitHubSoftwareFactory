using Djohnnie.SoftwareFactory.Contracts.AiModels;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed class GetModelUsageValidator : AbstractValidator<GetModelUsageQuery>
{
    public GetModelUsageValidator()
    {
        RuleFor(request => request.Dto.ModelId).NotEmpty();
        RuleFor(request => request.Dto.Page).GreaterThanOrEqualTo(0);
        RuleFor(request => request.Dto.PageSize).InclusiveBetween(1, AiModelUsageQuery.MaxPageSize);
    }
}
