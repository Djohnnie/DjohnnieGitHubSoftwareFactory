using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.ProjectChats;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.ProjectChats;

public sealed class StartProjectChatValidator : AbstractValidator<StartProjectChatCommand>
{
    public StartProjectChatValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Title).NotEmpty().MaximumLength(ProjectChat.MaxTitleLength);
    }
}
