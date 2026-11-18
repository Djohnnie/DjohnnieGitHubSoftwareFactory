using Djohnnie.SoftwareFactory.Application.Common;
using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.ProjectChats;

public sealed class GetProjectChatPayloadsValidator : AbstractValidator<GetProjectChatPayloadsQuery>
{
    public GetProjectChatPayloadsValidator()
    {
        RuleFor(request => request.Dto.RepositoryFullName).ValidRepositoryFullName();
        RuleFor(request => request.Dto.Id).NotEmpty();
    }
}
