using FluentValidation;

namespace Djohnnie.SoftwareFactory.Application.Features.Assistant;

public sealed class AppendChatMessagesValidator : AbstractValidator<AppendChatMessagesCommand>
{
    public AppendChatMessagesValidator()
    {
        RuleFor(request => request.Dto.CopilotKey).NotEmpty().MaximumLength(100);
        RuleFor(request => request.Dto.Messages).NotEmpty();
    }
}
