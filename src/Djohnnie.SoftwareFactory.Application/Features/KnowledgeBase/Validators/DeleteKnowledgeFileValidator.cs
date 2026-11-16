using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

public sealed class DeleteKnowledgeFileValidator : AbstractValidator<DeleteKnowledgeFileCommand>
{
    public DeleteKnowledgeFileValidator()
    {
        RuleFor(request => request.Dto.Id).NotEmpty();
    }
}
