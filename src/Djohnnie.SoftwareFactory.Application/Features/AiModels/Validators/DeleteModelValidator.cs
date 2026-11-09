using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.AiModels;

public sealed class DeleteModelValidator : AbstractValidator<DeleteModelCommand>
{
    public DeleteModelValidator()
    {
        RuleFor(request => request.Dto.Id).NotEmpty();
    }
}
