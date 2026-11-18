using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.ProjectChats;

public sealed class DeleteProjectChatValidator : AbstractValidator<DeleteProjectChatCommand>
{
    public DeleteProjectChatValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Id).NotEmpty();
    }
}
