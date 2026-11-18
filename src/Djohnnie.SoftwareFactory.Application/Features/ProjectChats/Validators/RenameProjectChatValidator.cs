using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.ProjectChats;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.ProjectChats;

public sealed class RenameProjectChatValidator : AbstractValidator<RenameProjectChatCommand>
{
    public RenameProjectChatValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Id).NotEmpty();
        RuleFor(request => request.Dto.Title).NotEmpty().MaximumLength(ProjectChat.MaxTitleLength);
    }
}
