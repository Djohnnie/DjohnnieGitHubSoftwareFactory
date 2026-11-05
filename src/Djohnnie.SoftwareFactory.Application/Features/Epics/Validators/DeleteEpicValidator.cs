using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Epics;

public sealed class DeleteEpicValidator : AbstractValidator<DeleteEpicCommand>
{
    public DeleteEpicValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).NotEmpty().MaximumLength(200);
        RuleFor(request => request.Dto.Id).GreaterThan(0);
    }
}
