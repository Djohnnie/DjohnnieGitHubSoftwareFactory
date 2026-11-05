using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Epics;

public sealed class AcceptEpicValidator : AbstractValidator<AcceptEpicCommand>
{
    public AcceptEpicValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).NotEmpty().MaximumLength(200);
        RuleFor(request => request.Dto.Id).GreaterThan(0);
    }
}
