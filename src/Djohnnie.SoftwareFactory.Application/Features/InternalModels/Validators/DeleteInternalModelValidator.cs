using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.InternalModels;

public sealed class DeleteInternalModelValidator : AbstractValidator<DeleteInternalModelCommand>
{
    public DeleteInternalModelValidator()
    {
        RuleFor(request => request.Dto.Id).NotEmpty();
    }
}
