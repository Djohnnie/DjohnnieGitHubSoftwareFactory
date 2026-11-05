using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Epics;

public sealed class GetEpicsValidator : AbstractValidator<GetEpicsQuery>
{
    public GetEpicsValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).NotEmpty().MaximumLength(200);
    }
}
