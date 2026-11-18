using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.ProjectChats;

public sealed class SummarizeProjectChatValidator : AbstractValidator<SummarizeProjectChatCommand>
{
    public SummarizeProjectChatValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Id).NotEmpty();
    }
}
