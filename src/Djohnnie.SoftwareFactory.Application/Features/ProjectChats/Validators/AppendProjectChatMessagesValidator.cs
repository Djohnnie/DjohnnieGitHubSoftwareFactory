using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.ProjectChats;

public sealed class AppendProjectChatMessagesValidator : AbstractValidator<AppendProjectChatMessagesCommand>
{
    public AppendProjectChatMessagesValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Id).NotEmpty();
        RuleFor(request => request.Dto.Messages).NotEmpty();
    }
}
